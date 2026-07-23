using System.Diagnostics;

namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Records bounded request diagnostics without inspecting credentials, query strings, or bodies.
/// </summary>
public sealed partial class RequestTelemetryMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<RequestTelemetryMiddleware> logger;

    /// <summary>Initializes a new safe request telemetry middleware instance.</summary>
    /// <param name="next">The next request handler.</param>
    /// <param name="logger">The structured logger used for safe completion events.</param>
    public RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    /// <summary>Records request telemetry around the remaining pipeline.</summary>
    /// <param name="context">The inbound HTTP context.</param>
    /// <returns>A task that completes after completion telemetry is emitted.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var route = TelemetryEndpointClassifier.Classify(context.Request.Path);
        var method = TelemetryRedactor.HttpMethod(context.Request.Method);
        var correlation = (CorrelationIdentifier?)context.Items[typeof(CorrelationIdentifier)];
        using var activity = BridgeTelemetry.ActivitySource.StartActivity("bridge.request");
        activity?.SetTag("bridge.route", route);
        activity?.SetTag("bridge.method", method);
        activity?.SetTag("bridge.correlation_id", correlation?.Value);

        try
        {
            await next(context);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("bridge.result", "failure");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var statusClass = TelemetryRedactor.ResultCategory(context.Response.StatusCode);
            var result = context.Response.StatusCode >= StatusCodes.Status400BadRequest ? "failure" : "success";
            activity?.SetTag("http.response.status_code", context.Response.StatusCode);
            activity?.SetTag("bridge.result", result);
            BridgeTelemetry.RequestCount.Add(1, new KeyValuePair<string, object?>("route", route), new KeyValuePair<string, object?>("status", statusClass));
            BridgeTelemetry.RequestDurationMilliseconds.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("route", route), new KeyValuePair<string, object?>("status", statusClass));
            LogRequestCompleted(logger, route, method, context.Response.StatusCode, statusClass, result, stopwatch.Elapsed.TotalMilliseconds, correlation?.Value);
        }
    }

    [LoggerMessage(LogLevel.Information, "Bridge request {Method} completed for {Route} with {StatusCode} ({StatusClass}, {Result}) in {ElapsedMilliseconds} ms, correlation {CorrelationId}")]
    private static partial void LogRequestCompleted(ILogger logger, string route, string method, int statusCode, string statusClass, string result, double elapsedMilliseconds, string? correlationId);
}
