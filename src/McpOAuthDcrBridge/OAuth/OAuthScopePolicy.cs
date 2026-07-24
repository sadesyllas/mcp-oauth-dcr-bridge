using System.Collections.Immutable;
using McpOAuthDcrBridge.Configuration;

namespace McpOAuthDcrBridge.OAuth;

/// <summary>
/// Validates OAuth scope strings against the optional configured scope allowlist without rewriting approved values.
/// </summary>
public static class OAuthScopePolicy
{
    /// <summary>Determines whether every scope token in <paramref name="scope"/> is a valid token permitted by <paramref name="allowedScopes"/>.</summary>
    /// <param name="scope">The space-delimited scope string exactly as supplied by the caller.</param>
    /// <param name="allowedScopes">The configured scope allowlist; an empty set means scopes are unrestricted.</param>
    /// <returns><see langword="true"/> when every token is a valid scope token and, if the allowlist is non-empty, permitted by it.</returns>
    public static bool IsAllowed(string scope, ImmutableHashSet<string> allowedScopes)
    {
        var tokens = scope.Split(' ', StringSplitOptions.None);
        return tokens.Length > 0 && tokens.All(OAuthScopeToken.IsValid) && (allowedScopes.Count == 0 || tokens.All(allowedScopes.Contains));
    }
}
