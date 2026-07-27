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

    /// <summary>Gets the permit count for protected OAuth endpoints.</summary>
    public required int RateLimitPermitLimit { get; init; }

    /// <summary>Gets the rate-limit replenishment interval.</summary>
    public required TimeSpan RateLimitWindow { get; init; }

    /// <summary>Gets the validity duration of a freshly generated private_key_jwt client assertion.</summary>
    public required TimeSpan PrivateKeyJwtAssertionLifetime { get; init; }
}
