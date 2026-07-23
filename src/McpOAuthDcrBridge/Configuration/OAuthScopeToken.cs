namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Validates the RFC 6749 scope-token grammar without normalizing accepted values.
/// </summary>
public static class OAuthScopeToken
{
    /// <summary>Determines whether text is one nonempty OAuth scope token.</summary>
    /// <param name="value">The candidate scope token.</param>
    /// <returns><see langword="true"/> only for permitted ASCII scope-token characters.</returns>
    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && value.All(character => character == '!' || character is >= '#' and <= '[' || character is >= ']' and <= '~');
}
