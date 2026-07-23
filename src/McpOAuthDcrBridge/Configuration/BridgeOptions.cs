using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Contains the validated, immutable deployment contract for one bridge instance.
/// </summary>
public sealed class BridgeOptions
{
    /// <summary>Gets the canonical public base URI for this bridge.</summary>
    public required Uri ExternalBaseUri { get; init; }

    /// <summary>Gets the fixed upstream authorization endpoint.</summary>
    public required Uri UpstreamAuthorizationEndpoint { get; init; }

    /// <summary>Gets the fixed upstream token endpoint.</summary>
    public required Uri UpstreamTokenEndpoint { get; init; }

    /// <summary>Gets the fixed upstream MCP endpoint.</summary>
    public required Uri UpstreamMcpUri { get; init; }

    /// <summary>Gets the fixed OAuth client identifier used on both sides of the bridge.</summary>
    public required string ClientId { get; init; }

    /// <summary>Gets the exact downstream callback URIs accepted by the bridge.</summary>
    public required ImmutableHashSet<string> AllowedRedirectUris { get; init; }

    /// <summary>Gets the optional allowed scope tokens; an empty set means scopes are unrestricted.</summary>
    public required ImmutableHashSet<string> AllowedScopes { get; init; }

    /// <summary>Gets the configured upstream token endpoint client authentication. It is excluded from JSON diagnostics because it can contain credentials.</summary>
    [JsonIgnore]
    public UpstreamClientAuthenticationOptions ClientAuthentication { get; init; } = null!;

    /// <summary>Gets static headers applied only to upstream MCP requests. They are excluded from JSON diagnostics because their values can be sensitive.</summary>
    [JsonIgnore]
    public ImmutableDictionary<string, ImmutableArray<string>> UpstreamMcpHeaders { get; init; } = null!;

    /// <summary>Gets bounded request, timeout, rate-limit, and shutdown settings.</summary>
    public required BridgeLimits Limits { get; init; }

    /// <summary>Gets the optional OTLP collector endpoint for metrics and traces.</summary>
    public Uri? OtlpEndpoint { get; init; }

    /// <summary>Gets the canonical bridge issuer URI.</summary>
    public Uri IssuerUri => ExternalBaseUri;

    /// <summary>Gets the canonical protected MCP resource URI.</summary>
    public Uri McpResourceUri => PublicUri("mcp");

    /// <summary>Gets the canonical dynamic client registration URI.</summary>
    public Uri RegistrationUri => PublicUri("register");

    /// <summary>Gets the canonical authorization endpoint URI.</summary>
    public Uri AuthorizationUri => PublicUri("authorize");

    /// <summary>Gets the canonical token endpoint URI.</summary>
    public Uri TokenUri => PublicUri("token");

    private Uri PublicUri(string path) => new(ExternalBaseUri, path);
}
