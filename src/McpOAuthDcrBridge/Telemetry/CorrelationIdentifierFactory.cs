using System.Text.RegularExpressions;

namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Validates inbound correlation identifiers and creates safe replacements when necessary.
/// </summary>
public static partial class CorrelationIdentifierFactory
{
    /// <summary>Returns a valid caller-supplied identifier or a newly generated identifier.</summary>
    /// <param name="candidate">The raw inbound header value.</param>
    /// <returns>A bounded identifier safe for logs, headers, metrics, and traces.</returns>
    public static CorrelationIdentifier Create(string? candidate) => new() { Value = IsValid(candidate) ? candidate! : Guid.NewGuid().ToString("N") };

    /// <summary>Determines whether an inbound correlation identifier is safe to propagate.</summary>
    /// <param name="candidate">The raw inbound header value.</param>
    /// <returns><see langword="true"/> only for bounded visible identifier characters.</returns>
    public static bool IsValid(string? candidate) => candidate is not null && CandidatePattern().IsMatch(candidate);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CandidatePattern();
}
