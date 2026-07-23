namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Validates configured HTTP field values before they reach the forwarding layer.
/// </summary>
public static class HttpFieldValue
{
    /// <summary>Determines whether text contains only permitted HTTP field-value characters.</summary>
    /// <param name="value">The candidate field value.</param>
    /// <returns><see langword="true"/> when the value is nonempty and excludes controls, including CR and LF.</returns>
    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && value.All(character => character == '\t' || (character is >= ' ' and <= '~') || (character is >= '\u0080' and <= '\u00ff'));
}
