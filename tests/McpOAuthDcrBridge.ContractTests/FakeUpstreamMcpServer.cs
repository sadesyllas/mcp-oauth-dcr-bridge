using Microsoft.Extensions.Primitives;

namespace McpOAuthDcrBridge.ContractTests;

/// <summary>
/// An in-process fake upstream MCP resource server used to observe exactly what the bridge forwards
/// and to script upstream responses (including bearer challenges and malformed bodies), without any
/// real network dependency.
/// </summary>
internal sealed class FakeUpstreamMcpServer : IAsyncDisposable
{
    private readonly WebApplication application;
    private int requestCount;

    private FakeUpstreamMcpServer(WebApplication application, string mcpPath)
    {
        this.application = application;
        McpPath = mcpPath;
    }

    /// <summary>Gets the exact upstream path this server exposes for MCP requests.</summary>
    public string McpPath { get; }

    /// <summary>Gets the method of the most recent request, or <see langword="null"/> before any request.</summary>
    public string? LastMethod { get; private set; }

    /// <summary>Gets the exact path of the most recent request.</summary>
    public string? LastPath { get; private set; }

    /// <summary>Gets the exact query string of the most recent request.</summary>
    public string? LastQuery { get; private set; }

    /// <summary>Gets the headers of the most recent request, snapshotted after the request completed.</summary>
    public Dictionary<string, StringValues>? LastHeaders { get; private set; }

    /// <summary>Gets the body of the most recent request.</summary>
    public string? LastBody { get; private set; }

    /// <summary>Gets the number of requests this server has received. Safe to read under concurrent requests.</summary>
    public int RequestCount => requestCount;

    /// <summary>Gets or sets the response behavior; defaults to a minimal successful text response.</summary>
    public Func<HttpContext, Task>? OnRequest { get; set; }

    /// <summary>Gets the absolute base URL (scheme, host, and port only) for this server.</summary>
    public string BaseUrl => application.Urls.Single();

    /// <summary>Gets the absolute MCP endpoint URL for this server.</summary>
    public string McpEndpoint => $"{BaseUrl}{McpPath}";

    /// <summary>Starts a fake upstream MCP server on an ephemeral local port.</summary>
    /// <param name="mcpPath">The exact upstream path to expose, matching a deployment's configured base path.</param>
    public static async Task<FakeUpstreamMcpServer> StartAsync(string mcpPath = "/api/streamable")
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        var application = builder.Build();
        var server = new FakeUpstreamMcpServer(application, mcpPath);
        application.MapMethods(mcpPath, ["GET", "POST", "DELETE"], async context =>
        {
            Interlocked.Increment(ref server.requestCount);
            server.LastMethod = context.Request.Method;
            server.LastPath = context.Request.Path.Value;
            server.LastQuery = context.Request.QueryString.Value;
            server.LastHeaders = context.Request.Headers.ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase);
            using var reader = new StreamReader(context.Request.Body);
            server.LastBody = await reader.ReadToEndAsync();
            if (server.OnRequest is { } onRequest)
            {
                await onRequest(context);
            }
            else
            {
                await context.Response.WriteAsync("ok");
            }
        });
        await application.StartAsync();
        return server;
    }

    /// <summary>Stops the fake server and releases its resources.</summary>
    public async ValueTask DisposeAsync() => await application.StopAsync();
}
