using Microsoft.Extensions.Primitives;

namespace McpOAuthDcrBridge.OAuth;

/// <summary>
/// Provides the single-value lookup and duplicate detection shared by the query-string parsing used
/// by the authorization endpoint and the form parsing used by the token endpoint.
/// </summary>
public static class OAuthFormParameters
{
    /// <summary>Determines whether any of the named parameters occurs more than once.</summary>
    /// <param name="parameters">The parsed query-string or form parameters.</param>
    /// <param name="names">The parameter names the caller treats as security relevant.</param>
    /// <returns><see langword="true"/> when any named parameter has more than one value.</returns>
    public static bool HasDuplicate(IEnumerable<KeyValuePair<string, StringValues>> parameters, IReadOnlySet<string> names) =>
        parameters.Any(parameter => names.Contains(parameter.Key) && parameter.Value.Count > 1);

    /// <summary>Attempts to read exactly one occurrence of a named parameter.</summary>
    /// <param name="parameters">The parsed query-string or form parameters.</param>
    /// <param name="name">The parameter name to read.</param>
    /// <param name="value">The single value, or an empty string when the parameter is absent or ambiguous.</param>
    /// <returns><see langword="true"/> only when the parameter occurs exactly once with exactly one value.</returns>
    public static bool TrySingleValue(IEnumerable<KeyValuePair<string, StringValues>> parameters, string name, out string value)
    {
        value = string.Empty;
        var matches = parameters.Where(parameter => parameter.Key == name).Select(parameter => parameter.Value).ToArray();
        if (matches.Length != 1 || matches[0].Count != 1 || matches[0][0] is not { } single) return false;
        value = single;
        return true;
    }
}
