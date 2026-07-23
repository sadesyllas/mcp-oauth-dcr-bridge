namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Represents a bounded startup configuration validation error that never includes configured values.
/// </summary>
public sealed class BridgeConfigurationException : Exception
{
    /// <summary>Initializes a new configuration error for the supplied configuration key.</summary>
    /// <param name="configurationKey">The key whose value failed validation.</param>
    /// <param name="reason">A non-sensitive explanation of the failed constraint.</param>
    public BridgeConfigurationException(string configurationKey, string reason)
        : base($"Invalid configuration at '{configurationKey}': {reason}")
    {
    }
}
