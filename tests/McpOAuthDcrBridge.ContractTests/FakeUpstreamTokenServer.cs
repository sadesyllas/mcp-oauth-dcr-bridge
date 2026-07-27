namespace McpOAuthDcrBridge.ContractTests;

/// <summary>
/// An in-process fake upstream OAuth token endpoint used to observe exactly what the bridge forwards
/// and to script upstream responses, without any real network dependency.
/// </summary>
internal sealed class FakeUpstreamTokenServer : IAsyncDisposable
{
    private readonly WebApplication application;

    private FakeUpstreamTokenServer(WebApplication application) => this.application = application;

    /// <summary>Gets the form received on the most recent request, or <see langword="null"/> before any request.</summary>
    public IFormCollection? LastForm { get; private set; }

    /// <summary>Gets the raw <c>Authorization</c> header value received on the most recent request.</summary>
    public string? LastAuthorizationHeader { get; private set; }

    /// <summary>Gets the number of requests this server has received.</summary>
    public int RequestCount { get; private set; }

    /// <summary>Gets or sets the response behavior; defaults to a minimal opaque-token success response.</summary>
    public Func<HttpContext, Task>? OnRequest { get; set; }

    /// <summary>Gets the absolute token endpoint URL for this server.</summary>
    public string TokenEndpoint => $"{application.Urls.Single()}/token";

    /// <summary>Starts a fake upstream token server on an ephemeral local port.</summary>
    public static async Task<FakeUpstreamTokenServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        var application = builder.Build();
        var server = new FakeUpstreamTokenServer(application);
        application.MapPost("/token", async context =>
        {
            server.RequestCount++;
            server.LastForm = await context.Request.ReadFormAsync();
            server.LastAuthorizationHeader = context.Request.Headers.Authorization.ToString();
            if (server.OnRequest is { } onRequest)
            {
                await onRequest(context);
            }
            else
            {
                await context.Response.WriteAsJsonAsync(new { access_token = "opaque-upstream-token", token_type = "Bearer", expires_in = 3600 });
            }
        });
        await application.StartAsync();
        return server;
    }

    /// <summary>Stops the fake server and releases its resources.</summary>
    public async ValueTask DisposeAsync() => await application.StopAsync();
}
