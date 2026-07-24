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
    public async Task SharedCaptureHarnessLocksM2TelemetryAndM4RegistrationCanaryContracts()
    {
        var canaries = new[]
        {
            "configured-secret-canary-1fa31", "registration-secret-canary-2b940", "invalid-redirect-canary-4c882",
            "unsupported-scope-canary-8a9e7", "authorization-canary-5ca22", "oauth-query-canary-70bd1",
            "cookie-canary-10a2f", "exception-canary-65cb4", "/run/secrets/certificate-canary-9bc44.pfx",
            "configured-header-canary-c1c71", "response-canary-20a8b", "custom-header-canary-3f173",
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
        application.MapGet("/test-throw", (HttpContext _) => throw new InvalidOperationException(canaries[7]));
        application.MapGet("/test-response", () => Results.Text(canaries[10]));
        application.MapGet("/test-rejected-log", (ILoggerFactory factory) =>
        {
            LogRejectedCategory(factory.CreateLogger("Framework.Future.Category"), canaries[4]);
            return Results.NoContent();
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };

        var registrationCases = new[]
        {
            $"{{\"redirect_uris\":[\"https://client.example.test/callback\"],\"client_secret\":\"{canaries[1]}\"}}",
            $"{{\"redirect_uris\":[\"https://client.example.test/{canaries[2]}\"]}}",
            $"{{\"redirect_uris\":[\"https://client.example.test/callback\"],\"scope\":\"{canaries[3]}\"}}",
        };
        var registrationArtifacts = new List<CapturedResponse>();
        foreach (var json in registrationCases)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/register?client_id={canaries[5]}&redirect_uri={canaries[2]}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {canaries[4]}");
            request.Headers.TryAddWithoutValidation("Cookie", $"session={canaries[6]}");
            request.Headers.TryAddWithoutValidation("X-Custom-Canary", canaries[11]);
            using var response = await client.SendAsync(request);
            registrationArtifacts.Add(await CaptureResponseAsync(response));
        }
        var registrationLogs = capture.Logs.ToArray();
        var registrationActivities = capture.Activities.ToArray();
        var registrationMeasurements = capture.Measurements.ToArray();
        using var exceptionHttpResponse = await client.GetAsync($"/test-throw?code={canaries[5]}");
        var exceptionResponse = await CaptureResponseAsync(exceptionHttpResponse);
        using var responseCanaryHttpResponse = await client.GetAsync("/test-response");
        var responseCanaryResponse = await CaptureResponseAsync(responseCanaryHttpResponse);
        using var rejectedLogHttpResponse = await client.GetAsync($"/test-rejected-log?code={canaries[5]}");
        var rejectedLogResponse = await CaptureResponseAsync(rejectedLogHttpResponse);
        var healthArtifacts = new[] { await CaptureResponseAsync(client, "/health/live"), await CaptureResponseAsync(client, "/health/ready") };
        for (var index = 0; index < 100; index++)
        {
            using var hostile = new HttpRequestMessage((index % 4) switch { 0 => HttpMethod.Get, 1 => HttpMethod.Post, 2 => HttpMethod.Delete, _ => HttpMethod.Patch }, $"/hostile-{index}?input={canaries[4]}-{index}");
            hostile.Headers.Host = $"host-{index}.example.test";
            hostile.Headers.TryAddWithoutValidation("X-Forwarded-Host", $"forwarded-{index}.example.test");
            hostile.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
            hostile.Headers.TryAddWithoutValidation("Forwarded", $"host=forwarded-{index}.example.test;proto=http");
            hostile.Headers.TryAddWithoutValidation("X-Correlation-ID", $"invalid correlation {index} {canaries[5]}");
            hostile.Headers.TryAddWithoutValidation("X-Custom-Canary", canaries[11]);
            using var _ = await client.SendAsync(hostile);
        }

        AssertRegistrationError(registrationArtifacts[0], "invalid_client_metadata");
        AssertRegistrationError(registrationArtifacts[1], "invalid_redirect_uri");
        AssertRegistrationError(registrationArtifacts[2], "invalid_client_metadata");
        Assert.Equal(3, registrationLogs.Length);
        Assert.Equal(3, registrationActivities.Length);
        Assert.Equal(6, registrationMeasurements.Length);
        Assert.All(registrationLogs, entry => AssertRegistrationLog(entry));
        Assert.All(registrationActivities, AssertRegistrationActivity);
        Assert.All(registrationMeasurements, AssertRegistrationMeasurement);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, exceptionResponse.StatusCode);
        Assert.Equal(canaries[10], responseCanaryResponse.Body);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, rejectedLogResponse.StatusCode);
        Assert.All(healthArtifacts, artifact =>
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, artifact.StatusCode);
            Assert.Equal("text/plain", artifact.ContentType);
            Assert.Equal("Healthy", artifact.Body);
            Assert.Single(artifact.Headers, header => header.Key == "X-Correlation-ID");
        });
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
            Assert.Equal(393314459, entry.EventId.Id);
            Assert.Null(entry.Exception);
            Assert.Equal("Bridge request {Method} completed for {Route} with {StatusCode} ({StatusClass}, {Result}) in {ElapsedMilliseconds} ms, correlation {CorrelationId}", entry.State["{OriginalFormat}"]);
        });
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "registration" && entry.State["StatusCode"] == "400" && entry.State["StatusClass"] == "4xx" && entry.State["Result"] == "failure");
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "other" && entry.State["StatusCode"] == "500" && entry.State["StatusClass"] == "5xx" && entry.State["Result"] == "failure");
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "health_live" && entry.State["StatusCode"] == "200" && entry.State["StatusClass"] == "2xx" && entry.State["Result"] == "success");
        Assert.DoesNotContain(capture.Logs, entry => entry.ToString().Contains(canaries[7], StringComparison.Ordinal));
        Assert.Contains(capture.Activities, activity => activity.Status == ActivityStatusCode.Error && activity.Tags.TryGetValue("bridge.route", out var route) && route == "registration" && activity.Tags.TryGetValue("bridge.result", out var result) && result == "failure");
        Assert.All(capture.Activities, activity =>
        {
            Assert.Equal(["bridge.correlation_id", "bridge.method", "bridge.result", "bridge.route", "http.response.status_code"], activity.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.Empty(activity.Events);
            Assert.Empty(activity.Baggage);
        });
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

        var telemetryArtifacts = FlattenArtifacts(capture.Logs, capture.Activities, capture.Measurements);
        var httpArtifacts = FlattenArtifacts(registrationArtifacts.Concat(healthArtifacts).Concat([exceptionResponse, responseCanaryResponse, rejectedLogResponse]));
        AssertCanariesAreAbsent(canaries, telemetryArtifacts, canaries[10]);
        AssertCanariesAreAbsent(canaries, httpArtifacts, canaries[10]);

        using var privateKeyJwtApplication = McpOAuthDcrBridge.BridgeApplication.Build(ValidBridgeCommandLine.Create("private_key_jwt", certificatePath: canaries[8]), null, logging => logging.AddProvider(capture.LoggerProvider));
        await privateKeyJwtApplication.StartAsync();
        using var privateKeyJwtClient = new HttpClient { BaseAddress = new Uri(privateKeyJwtApplication.Urls.Single()) };
        using var privateKeyJwtHealthResponse = await privateKeyJwtClient.GetAsync("/health/ready");
        var privateKeyJwtHealth = await CaptureResponseAsync(privateKeyJwtHealthResponse);
        Assert.Equal(System.Net.HttpStatusCode.OK, privateKeyJwtHealth.StatusCode);
        await privateKeyJwtApplication.StopAsync();
        Assert.DoesNotContain(canaries[8], FlattenArtifacts(capture.Logs, capture.Activities, capture.Measurements, [privateKeyJwtHealth]), StringComparison.Ordinal);

        await application.StopAsync();
    }

    private static async Task<CapturedResponse> CaptureResponseAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return await CaptureResponseAsync(response);
    }

    private static async Task<CapturedResponse> CaptureResponseAsync(HttpResponseMessage response) => new(
        response.StatusCode,
        response.Content.Headers.ContentType?.MediaType,
        response.Headers.Concat(response.Content.Headers).ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
        await response.Content.ReadAsStringAsync());

    private static void AssertRegistrationError(CapturedResponse artifact, string error)
    {
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, artifact.StatusCode);
        Assert.Equal("application/json", artifact.ContentType);
        Assert.Equal($"{{\"error\":\"{error}\",\"error_description\":\"invalid client metadata\"}}", artifact.Body);
        Assert.DoesNotContain("WWW-Authenticate", artifact.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertRegistrationLog(CapturedLog entry)
    {
        Assert.Equal("registration", entry.State["Route"]);
        Assert.Equal("400", entry.State["StatusCode"]);
        Assert.Equal("4xx", entry.State["StatusClass"]);
        Assert.Equal("failure", entry.State["Result"]);
    }

    private static void AssertRegistrationActivity(CapturedActivity activity)
    {
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("registration", activity.Tags["bridge.route"]);
        Assert.Equal("failure", activity.Tags["bridge.result"]);
        Assert.Empty(activity.Events);
        Assert.Empty(activity.Baggage);
    }

    private static void AssertRegistrationMeasurement(CapturedMeasurement measurement)
    {
        Assert.True(measurement.Name is "bridge.requests" or "bridge.request.duration");
        Assert.Equal("registration", measurement.Tags["route"]);
        Assert.Equal("4xx", measurement.Tags["status"]);
        Assert.Equal(measurement.Name == "bridge.requests" ? "long" : "double", measurement.Kind);
    }

    private static void AssertCanariesAreAbsent(IEnumerable<string> canaries, string artifacts, params string[] exclusions)
    {
        foreach (var canary in canaries.Except(exclusions, StringComparer.Ordinal))
        {
            Assert.DoesNotContain(canary, artifacts, StringComparison.Ordinal);
        }
    }

    private static string FlattenArtifacts(IEnumerable<CapturedLog> logs, IEnumerable<CapturedActivity> activities, IEnumerable<CapturedMeasurement> measurements, IEnumerable<CapturedResponse>? responses = null) => string.Join('\n', logs.Select(entry => entry.ToString()).Concat(activities.Select(activity => activity.ToString())).Concat(measurements.Select(measurement => measurement.ToString())).Concat(responses?.Select(response => response.ToString()) ?? []));

    private static string FlattenArtifacts(IEnumerable<CapturedResponse> responses) => string.Join('\n', responses.Select(response => response.ToString()));

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
                ActivityStopped = activity => Activities.Enqueue(new CapturedActivity(
                    activity.Status,
                    activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? string.Empty, StringComparer.Ordinal),
                    activity.Events.SelectMany(activityEvent => activityEvent.Tags.Select(tag => new KeyValuePair<string, string>($"{activityEvent.Name}:{tag.Key}", tag.Value?.ToString() ?? string.Empty))).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                    activity.Baggage.ToDictionary(item => item.Key, item => item.Value ?? string.Empty, StringComparer.Ordinal))),
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
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => entries.Enqueue(new CapturedLog(category, logLevel, eventId, exception?.ToString(), formatter(state, exception), ToState(state)));
        private static Dictionary<string, string> ToState<TState>(TState state) => state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToDictionary(field => field.Key, field => field.Value?.ToString() ?? string.Empty, StringComparer.Ordinal) : [];
    }

    private sealed record CapturedResponse(System.Net.HttpStatusCode StatusCode, string? ContentType, Dictionary<string, string> Headers, string Body)
    {
        public override string ToString() => $"{StatusCode} {ContentType} {string.Join(';', Headers.Select(header => $"{header.Key}={header.Value}"))} {Body}";
    }

    private sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string? Exception, string Message, Dictionary<string, string> State)
    {
        public override string ToString() => $"{Category} {Level} {Exception} {Message} {string.Join(';', State.Select(field => $"{field.Key}={field.Value}"))}";
    }

    private sealed record CapturedActivity(ActivityStatusCode Status, Dictionary<string, string> Tags, Dictionary<string, string> Events, Dictionary<string, string> Baggage)
    {
        public override string ToString() => $"{Status} {string.Join(';', Tags.Select(tag => $"{tag.Key}={tag.Value}"))} {string.Join(';', Events.Select(item => $"{item.Key}={item.Value}"))} {string.Join(';', Baggage.Select(item => $"{item.Key}={item.Value}"))}";
    }

    private sealed record CapturedMeasurement(string Name, string Kind, Dictionary<string, string> Tags)
    {
        public override string ToString() => $"{Name} {Kind} {string.Join(';', Tags.Select(tag => $"{tag.Key}={tag.Value}"))}";
    }
}
