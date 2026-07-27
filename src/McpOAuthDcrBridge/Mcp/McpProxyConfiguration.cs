using System.Net;
using McpOAuthDcrBridge.Configuration;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace McpOAuthDcrBridge.Mcp;

/// <summary>
/// Builds the single fixed YARP route and cluster that proxy <c>/mcp</c> to the configured upstream
/// MCP server, and the transforms that apply configured static headers and rewrite an upstream bearer
/// challenge to identify the bridge's own discovery metadata.
/// </summary>
public static class McpProxyConfiguration
{
    private const string RouteAndClusterId = "mcp";
    private static readonly string[] PreservedChallengeParameterNames = ["error", "error_description", "scope"];

    /// <summary>Registers YARP with exactly one route and cluster derived from validated bridge options.</summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="options">The validated bridge configuration.</param>
    /// <returns>The same service collection for composition.</returns>
    public static IServiceCollection AddMcpReverseProxy(this IServiceCollection services, BridgeOptions options)
    {
        var route = new RouteConfig
        {
            RouteId = RouteAndClusterId,
            ClusterId = RouteAndClusterId,
            Match = new RouteMatch { Path = "/mcp", Methods = ["GET", "POST", "DELETE"] },
        }.WithTransform(transform => transform["PathSet"] = options.UpstreamMcpUri.AbsolutePath);
        var cluster = new ClusterConfig
        {
            ClusterId = RouteAndClusterId,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
            {
                [RouteAndClusterId] = new DestinationConfig { Address = OriginOf(options.UpstreamMcpUri) },
            },
        };

        services.AddReverseProxy()
            .LoadFromMemory([route], [cluster])
            .AddTransforms(builderContext =>
            {
                builderContext.AddRequestTransform(transformContext => ApplyUpstreamHeaders(transformContext, options));
                builderContext.AddResponseTransform(transformContext => RewriteBearerChallenge(transformContext, options));
            });
        return services;
    }

    private static ValueTask ApplyUpstreamHeaders(RequestTransformContext context, BridgeOptions options)
    {
        foreach (var header in options.UpstreamMcpHeaders)
        {
            if (ForbiddenUpstreamMcpHeaders.Contains(header.Key)) continue;
            context.ProxyRequest.Headers.Remove(header.Key);
            context.ProxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        return default;
    }

    private static ValueTask RewriteBearerChallenge(ResponseTransformContext context, BridgeOptions options)
    {
        if (context.ProxyResponse?.StatusCode != HttpStatusCode.Unauthorized) return default;
        var challenge = context.HttpContext.Response.Headers.WWWAuthenticate.ToString();
        if (!challenge.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)) return default;

        var parameters = BearerChallengeParameters.Parse(challenge.Length > "Bearer".Length ? challenge["Bearer".Length..].Trim() : null);
        var metadataUri = new Uri(options.IssuerUri, ".well-known/oauth-protected-resource").AbsoluteUri;
        var rewritten = new List<string> { $"resource_metadata=\"{metadataUri}\"" };
        foreach (var name in PreservedChallengeParameterNames)
        {
            if (parameters.TryGetValue(name, out var value))
            {
                rewritten.Add($"{name}=\"{value}\"");
            }
        }

        context.HttpContext.Response.Headers.WWWAuthenticate = $"Bearer {string.Join(", ", rewritten)}";
        return default;
    }

    private static string OriginOf(Uri uri) => new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.AbsoluteUri;
}
