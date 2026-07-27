namespace McpOAuthDcrBridge.Mcp;

/// <summary>
/// Parses the comma-separated <c>auth-param</c> list of an RFC 6750 <c>Bearer</c> challenge.
/// </summary>
public static class BearerChallengeParameters
{
    /// <summary>Parses <c>name="value"</c> pairs from a Bearer challenge's parameter text.</summary>
    /// <param name="parameters">The challenge text following the <c>Bearer</c> scheme token, or <see langword="null"/>.</param>
    /// <returns>The parsed parameters, keyed case-insensitively; empty when there is nothing to parse.</returns>
    public static IReadOnlyDictionary<string, string> Parse(string? parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(parameters))
        {
            return result;
        }

        foreach (var pair in parameters.Split(','))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = pair[..separatorIndex].Trim();
            var value = pair[(separatorIndex + 1)..].Trim().Trim('"');
            if (name.Length > 0)
            {
                result[name] = value;
            }
        }

        return result;
    }
}
