namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Represents the bounded, non-secret correlation identifier used for a bridge request.
/// </summary>
public sealed class CorrelationIdentifier
{
    /// <summary>Gets the HTTP header used to transport the correlation identifier.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Gets the accepted correlation identifier value.</summary>
    public required string Value { get; init; }
}
