using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

public sealed class TelemetryCaptureContractTests
{
    [Fact]
    public async Task AllCapturedTelemetryUsesOnlyTheBoundedRequestContract()
    {
        const string queryCanary = "query-canary-1fa31";
        const string headerCanary = "header-canary-4c882";
        const string bodyCanary = "body-canary-8a9e7";
        const string exceptionCanary = "exception-canary-5ca22";
        var logs = new CapturingLoggerProvider();
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "McpOAuthDcrBridge",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue,
        };
        using var meterListener = new MeterListener();
        var measurements = new ConcurrentQueue<MetricMeasurement>();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "McpOAuthDcrBridge") listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => measurements.Enqueue(new MetricMeasurement(instrument.Name, FormatMetricTags(tags.ToArray()))));
        meterListener.Start();

        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(ValidBridgeCommandLine.Arguments, null, logging => logging.AddProvider(logs));
        application.MapGet("/test-throw", (HttpContext _) => throw new InvalidOperationException(exceptionCanary));
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, $"/unknown?secret={queryCanary}")
        {
            Content = new StringContent(bodyCanary),
        };
        malformedRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {headerCanary}");
        using var malformedResponse = await client.SendAsync(malformedRequest);
        using var exceptionResponse = await client.GetAsync($"/test-throw?secret={queryCanary}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, malformedResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, exceptionResponse.StatusCode);
        Assert.All(logs.Entries, entry =>
        {
            Assert.Equal(typeof(McpOAuthDcrBridge.Telemetry.RequestTelemetryMiddleware).FullName, entry.Category);
            Assert.Contains("Bridge request", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(queryCanary, entry.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(headerCanary, entry.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(bodyCanary, entry.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(exceptionCanary, entry.ToString(), StringComparison.Ordinal);
        });
        Assert.Contains(logs.Entries, entry => entry.Message.Contains("404", StringComparison.Ordinal) && entry.Message.Contains("failure", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.Message.Contains("500", StringComparison.Ordinal) && entry.Message.Contains("failure", StringComparison.Ordinal));
        Assert.All(activities, activity => Assert.DoesNotContain(queryCanary, FormatTags(activity.Tags), StringComparison.Ordinal));
        Assert.All(measurements, measurement => Assert.DoesNotContain(queryCanary, measurement.Tags, StringComparison.Ordinal));
        Assert.Contains(measurements, measurement => measurement.Name == "bridge.requests" && measurement.Tags.Contains("route", StringComparison.Ordinal) && measurement.Tags.Contains("status", StringComparison.Ordinal));
        Assert.DoesNotContain(exceptionCanary, await exceptionResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await application.StopAsync();
    }

    private static string FormatTags(IEnumerable<KeyValuePair<string, string?>> tags) => string.Join(";", tags.Select(tag => $"{tag.Key}={tag.Value}"));

    private static string FormatMetricTags(IEnumerable<KeyValuePair<string, object?>> tags) => string.Join(";", tags.Select(tag => $"{tag.Key}={tag.Value}"));

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string category;
        private readonly ConcurrentQueue<LogEntry> entries;

        public CapturingLogger(string category, ConcurrentQueue<LogEntry> entries)
        {
            this.category = category;
            this.entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(category, formatter(state, exception), state?.ToString() ?? string.Empty));
    }

    private sealed record LogEntry(string Category, string Message, string State)
    {
        public override string ToString() => $"{Category} {Message} {State}";
    }

    private sealed record MetricMeasurement(string Name, string Tags);
}
