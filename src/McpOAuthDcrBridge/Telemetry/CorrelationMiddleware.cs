namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Establishes one bounded correlation identifier for each inbound bridge request.
/// </summary>
public sealed class CorrelationMiddleware
{
    private readonly RequestDelegate next;

    /// <summary>Initializes a new correlation middleware instance.</summary>
    /// <param name="next">The next request handler.</param>
    public CorrelationMiddleware(RequestDelegate next) => this.next = next;

    /// <summary>Processes a request and returns its safe correlation identifier to the caller.</summary>
    /// <param name="context">The inbound HTTP context.</param>
    /// <returns>A task that completes after the remaining pipeline has executed.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlation = CorrelationIdentifierFactory.Create(context.Request.Headers[CorrelationIdentifier.HeaderName].ToString());
        context.Items[typeof(CorrelationIdentifier)] = correlation;
        context.Response.Headers[CorrelationIdentifier.HeaderName] = correlation.Value;
        await next(context);
    }
}
