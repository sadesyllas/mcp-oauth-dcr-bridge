using McpOAuthDcrBridge.OAuth;

namespace McpOAuthDcrBridge.Token;

/// <summary>
/// Relays an upstream token-endpoint response to the downstream caller unchanged: the same status
/// code, content type, safe headers, and body, without token substitution or schema translation.
/// </summary>
public sealed class UpstreamTokenResponseResult : IResult
{
    private readonly HttpResponseMessage upstreamResponse;

    /// <summary>Initializes a relay result that takes ownership of one upstream response.</summary>
    /// <param name="upstreamResponse">The upstream response to relay; disposed after it is written.</param>
    public UpstreamTokenResponseResult(HttpResponseMessage upstreamResponse) => this.upstreamResponse = upstreamResponse;

    /// <summary>Writes the upstream status, safe headers, and body unchanged, streaming the body without buffering it.</summary>
    /// <param name="httpContext">The HTTP context receiving the result.</param>
    /// <returns>A task that completes once the body has been relayed.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        using (upstreamResponse)
        {
            httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
            foreach (var header in upstreamResponse.Headers)
            {
                if (!HopByHopHeaders.IsHopByHop(header.Key)) httpContext.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in upstreamResponse.Content.Headers)
            {
                if (!HopByHopHeaders.IsHopByHop(header.Key)) httpContext.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await upstreamResponse.Content.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        }
    }
}
