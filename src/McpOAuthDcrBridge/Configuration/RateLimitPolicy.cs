namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Describes the resolved fixed-window rate limit applied to one protected endpoint category.
/// </summary>
public sealed class RateLimitPolicy
{
    /// <summary>Gets the maximum permits granted per window.</summary>
    public required int PermitLimit { get; init; }

    /// <summary>Gets the fixed replenishment window.</summary>
    public required TimeSpan Window { get; init; }
}
