using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

public sealed partial class TelemetryCaptureContractTests
{
    private static readonly string[] MetricStatusClasses = ["2xx", "4xx", "5xx"];

    [Fact]
    public async Task CaptureHarnessLocksTelemetryHealthAndRegistrationCanaryContracts()
    {
        var canaries = new[]
        {
            "client-secret-canary-1fa31", "invalid-redirect-canary-4c882", "unsupported-scope-canary-8a9e7",
            "header-canary-5ca22", "query-canary-70bd1", "cookie-canary-10a2f", "exception-canary-65cb4",
            "/run/secrets/certificate-canary-9bc44.pfx", "configured-header-canary-c1c71", "response-canary-20a8b",
        };
        using var capture = new TelemetryCapture();
        var arguments = ValidBridgeCommandLine.Arguments.Concat([
            "--Bridge:AllowedScopes:0", "mcp.read",
            "--Bridge:Upstream:ClientAuthentication:Method", "client_secret_post",
            "--Bridge:Upstream:ClientAuthentication:ClientSecret", canaries[0],
            "--Bridge:Upstream:McpHeaders:0:Name", "X-Configured",
            "--Bridge:Upstream:McpHeaders:0:Values:0", canaries[8],
        ]).ToArray();
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(arguments, null, logging => logging.AddProvider(capture.LoggerProvider));
        application.MapGet("/test-throw", (HttpContext _) => throw new InvalidOperationException(canaries[6]));
        application.MapGet("/test-rejected-log", (ILoggerFactory factory) =>
        {
            LogRejectedCategory(factory.CreateLogger("Framework.Future.Category"), canaries[4]);
            return Results.NoContent();
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        var registrationCases = new[]
        {
            $"{{\"redirect_uris\":[\"https://client.example.test/callback\"],\"client_secret\":\"{canaries[0]}\"}}",
            $"{{\"redirect_uris\":[\"https://client.example.test/{canaries[1]}\"]}}",
            $"{{\"redirect_uris\":[\"https://client.example.test/callback\"],\"scope\":\"{canaries[2]}\"}}",
        };
        var registrationResponses = new List<HttpResponseMessage>();
        foreach (var json in registrationCases)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/register?input={canaries[4]}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {canaries[3]}");
            request.Headers.TryAddWithoutValidation("Cookie", $"session={canaries[5]}");
            registrationResponses.Add(await client.SendAsync(request));
        }
        using var exceptionResponse = await client.GetAsync($"/test-throw?input={canaries[4]}");
        using var rejectedLogResponse = await client.GetAsync($"/test-rejected-log?input={canaries[4]}");
        var healthArtifacts = new[] { await CaptureResponseAsync(client, "/health/live"), await CaptureResponseAsync(client, "/health/ready") };
        for (var index = 0; index < 100; index++)
        {
            using var hostile = new HttpRequestMessage((index % 4) switch { 0 => HttpMethod.Get, 1 => HttpMethod.Post, 2 => HttpMethod.Delete, _ => HttpMethod.Patch }, $"/hostile-{index}?input={canaries[4]}-{index}");
            hostile.Headers.Host = $"host-{index}.example.test";
            hostile.Headers.TryAddWithoutValidation("X-Forwarded-Host", $"forwarded-{index}.example.test");
            hostile.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
            hostile.Headers.TryAddWithoutValidation("Forwarded", $"host=forwarded-{index}.example.test;proto=http");
            hostile.Headers.TryAddWithoutValidation("X-Correlation-ID", $"invalid correlation {index}");
            using var _ = await client.SendAsync(hostile);
        }

        var registrationBodies = await Task.WhenAll(registrationResponses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.All(registrationResponses, response =>
        {
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        });
        Assert.Equal("{\"error\":\"invalid_client_metadata\",\"error_description\":\"invalid client metadata\"}", registrationBodies[0]);
        Assert.Equal("{\"error\":\"invalid_redirect_uri\",\"error_description\":\"invalid client metadata\"}", registrationBodies[1]);
        Assert.Equal("{\"error\":\"invalid_client_metadata\",\"error_description\":\"invalid client metadata\"}", registrationBodies[2]);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, exceptionResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, rejectedLogResponse.StatusCode);
        Assert.Equal((System.Net.HttpStatusCode.OK, "text/plain", "Healthy"), healthArtifacts[0]);
        Assert.Equal((System.Net.HttpStatusCode.OK, "text/plain", "Healthy"), healthArtifacts[1]);
        Assert.NotEmpty(capture.Logs);
        Assert.NotEmpty(capture.Activities);
        Assert.NotEmpty(capture.Measurements);
        Assert.All(capture.Logs, entry =>
        {
            Assert.Equal(typeof(McpOAuthDcrBridge.Telemetry.RequestTelemetryMiddleware).FullName, entry.Category);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal(
                ["CorrelationId", "ElapsedMilliseconds", "Method", "Result", "Route", "StatusClass", "StatusCode", "{OriginalFormat}"],
                entry.State.Keys.Order(StringComparer.Ordinal));
        });
        Assert.DoesNotContain(capture.Logs, entry => entry.ToString().Contains(canaries[6], StringComparison.Ordinal));
        Assert.Contains(capture.Activities, activity => activity.Status == ActivityStatusCode.Error && activity.Tags.TryGetValue("bridge.route", out var route) && route == "registration" && activity.Tags.TryGetValue("bridge.result", out var result) && result == "failure");
        Assert.All(capture.Activities, activity => Assert.Equal(["bridge.correlation_id", "bridge.method", "bridge.result", "bridge.route", "http.response.status_code"], activity.Tags.Keys.Order(StringComparer.Ordinal)));
        Assert.Contains(capture.Measurements, measurement => measurement.Name == "bridge.requests" && measurement.Kind == "long");
        Assert.Contains(capture.Measurements, measurement => measurement.Name == "bridge.request.duration" && measurement.Kind == "double");
        Assert.All(capture.Measurements.Where(measurement => measurement.Name is "bridge.requests" or "bridge.request.duration"), measurement => Assert.Equal(["route", "status"], measurement.Tags.Keys.Order(StringComparer.Ordinal)));
        var allowedRoutes = new HashSet<string>(["registration", "health_live", "health_ready", "other"], StringComparer.Ordinal);
        Assert.All(capture.Measurements.Where(measurement => measurement.Name is "bridge.requests" or "bridge.request.duration"), measurement =>
        {
            Assert.Contains(measurement.Tags["route"], allowedRoutes);
            Assert.Contains(measurement.Tags["status"], MetricStatusClasses);
        });
        Assert.True(capture.Measurements.Where(measurement => measurement.Name is "bridge.requests" or "bridge.request.duration").Select(measurement => $"{measurement.Name}:{measurement.Tags["route"]}:{measurement.Tags["status"]}").Distinct(StringComparer.Ordinal).Count() <= 24);

        var artifacts = string.Join('\n', capture.Logs.Select(entry => entry.ToString()).Concat(capture.Activities.Select(activity => activity.ToString())).Concat(capture.Measurements.Select(measurement => measurement.ToString())).Concat(registrationBodies).Append(await exceptionResponse.Content.ReadAsStringAsync()).Concat(healthArtifacts.Select(artifact => artifact.Item3)));
        foreach (var canary in canaries)
        {
            Assert.DoesNotContain(canary, artifacts, StringComparison.Ordinal);
        }

        foreach (var response in registrationResponses) response.Dispose();
        await application.StopAsync();
    }

    private static async Task<(System.Net.HttpStatusCode, string, string)> CaptureResponseAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return (response.StatusCode, response.Content.Headers.ContentType!.MediaType!, await response.Content.ReadAsStringAsync());
    }

    [LoggerMessage(LogLevel.Error, "Rejected test category {Canary}")]
    private static partial void LogRejectedCategory(ILogger logger, string canary);

    private sealed class TelemetryCapture : IDisposable
    {
        public ConcurrentQueue<CapturedLog> Logs { get; } = new();
        public ConcurrentQueue<CapturedActivity> Activities { get; } = new();
        public ConcurrentQueue<CapturedMeasurement> Measurements { get; } = new();
        public CapturingLoggerProvider LoggerProvider { get; }
        private readonly ActivityListener activityListener;
        private readonly MeterListener meterListener;

        public TelemetryCapture()
        {
            LoggerProvider = new CapturingLoggerProvider(Logs);
            activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "McpOAuthDcrBridge",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Activities.Enqueue(new CapturedActivity(activity.Status, activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? string.Empty, StringComparer.Ordinal))),
            };
            ActivitySource.AddActivityListener(activityListener);
            meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "McpOAuthDcrBridge") listener.EnableMeasurementEvents(instrument);
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Measurements.Enqueue(new CapturedMeasurement(instrument.Name, "long", ToTags(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Measurements.Enqueue(new CapturedMeasurement(instrument.Name, "double", ToTags(tags))));
            meterListener.Start();
        }

        public void Dispose()
        {
            meterListener.Dispose();
            activityListener.Dispose();
            LoggerProvider.Dispose();
        }

        private static Dictionary<string, string> ToTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) => tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<CapturedLog> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), ToState(state)));
        private static Dictionary<string, string> ToState<TState>(TState state) => state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToDictionary(field => field.Key, field => field.Value?.ToString() ?? string.Empty, StringComparer.Ordinal) : [];
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Message, Dictionary<string, string> State)
    {
        public override string ToString() => $"{Category} {Level} {Message} {string.Join(';', State.Select(field => $"{field.Key}={field.Value}"))}";
    }

    private sealed record CapturedActivity(ActivityStatusCode Status, Dictionary<string, string> Tags)
    {
        public override string ToString() => $"{Status} {string.Join(';', Tags.Select(tag => $"{tag.Key}={tag.Value}"))}";
    }

    private sealed record CapturedMeasurement(string Name, string Kind, Dictionary<string, string> Tags)
    {
        public override string ToString() => $"{Name} {Kind} {string.Join(';', Tags.Select(tag => $"{tag.Key}={tag.Value}"))}";
    }
}
