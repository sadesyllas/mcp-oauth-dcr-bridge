using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Defines the closed, bridge-owned boundary for safe log emission and configuration diagnostics.
/// </summary>
public static class SafeTelemetryPolicy
{
    private static readonly FrozenDictionary<string, LogLevel> MinimumLogLevels = new Dictionary<string, LogLevel>(StringComparer.Ordinal)
    {
        [typeof(RequestTelemetryMiddleware).FullName!] = LogLevel.Information,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Determines whether a log category and level are explicitly approved for bridge telemetry.
    /// </summary>
    /// <param name="providerName">The optional logger-provider name.</param>
    /// <param name="category">The log category.</param>
    /// <param name="level">The proposed log level.</param>
    /// <returns><see langword="true"/> only for registered bridge-owned categories at approved levels.</returns>
    public static bool IsEnabled(string? providerName, string? category, LogLevel level)
    {
        // LogLevel.None is a filter sentinel, not an emittable severity. Reject
        // it and future/invalid enum values before comparing minimum levels.
        if (level is < LogLevel.Trace or > LogLevel.Critical)
        {
            return false;
        }

        return category is not null &&
            MinimumLogLevels.TryGetValue(category, out var minimumLevel) &&
            level >= minimumLevel;
    }

    /// <summary>
    /// Creates a bounded configuration-validation message using only the configuration key.
    /// </summary>
    /// <param name="configurationKey">The validated configuration key.</param>
    /// <returns>A non-secret configuration diagnostic.</returns>
    public static string ConfigurationError(string configurationKey) => $"Configuration validation failed for {configurationKey}.";
}
