namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Validates HTTP field names against the RFC token grammar.
/// </summary>
public static class HttpFieldName
{
    /// <summary>Determines whether text is a nonempty HTTP field name.</summary>
    /// <param name="value">The candidate field name.</param>
    /// <returns><see langword="true"/> only for HTTP token characters.</returns>
    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && value.All(character => char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));
}
