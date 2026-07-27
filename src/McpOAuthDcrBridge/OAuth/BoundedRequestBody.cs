namespace McpOAuthDcrBridge.OAuth;

/// <summary>
/// Reads a request body up to a configured byte limit without relying on framework form or JSON
/// buffering, so oversized declared or chunked bodies are rejected before they are fully read.
/// </summary>
public static class BoundedRequestBody
{
    /// <summary>Reads the exact request body, enforcing a maximum size regardless of framing.</summary>
    /// <param name="request">The inbound HTTP request.</param>
    /// <param name="maximumBytes">The maximum number of bytes to accept.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>The request body bytes, or <see langword="null"/> when the declared or actual size exceeds <paramref name="maximumBytes"/>.</returns>
    public static async Task<byte[]?> ReadAsync(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > 0 and var length && length > maximumBytes) return null;
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximumBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
