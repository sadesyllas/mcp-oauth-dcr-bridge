using System.Collections.Immutable;

namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Contains validated resource limits used to bound bridge work.
/// </summary>
public sealed class BridgeLimits
{
    /// <summary>Gets the maximum DCR JSON request size in bytes.</summary>
    public required int DcrRequestBodyBytes { get; init; }

    /// <summary>Gets the maximum OAuth form request size in bytes.</summary>
    public required int TokenRequestBodyBytes { get; init; }

    /// <summary>Gets the outbound OAuth request timeout.</summary>
    public required TimeSpan OAuthTimeout { get; init; }

    /// <summary>Gets the maximum idle time for an MCP stream.</summary>
    public required TimeSpan McpActivityTimeout { get; init; }

    /// <summary>Gets the graceful shutdown drain time.</summary>
    public required TimeSpan ShutdownDrainTimeout { get; init; }

    /// <summary>Gets the default permit count for protected OAuth endpoints, used where no per-endpoint override is configured.</summary>
    public required int RateLimitPermitLimit { get; init; }

    /// <summary>Gets the default rate-limit replenishment interval, used where no per-endpoint override is configured.</summary>
    public required TimeSpan RateLimitWindow { get; init; }

    /// <summary>Gets the independently resolved rate limit for each protected endpoint, keyed by its policy name ("dcr", "authorize", "token").</summary>
    public required ImmutableDictionary<string, RateLimitPolicy> EndpointRateLimits { get; init; }

    /// <summary>Gets the validity duration of a freshly generated private_key_jwt client assertion.</summary>
    public required TimeSpan PrivateKeyJwtAssertionLifetime { get; init; }
}
