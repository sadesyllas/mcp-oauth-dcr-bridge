namespace McpOAuthDcrBridge.Mcp;

/// <summary>
/// Parses the comma-separated <c>auth-param</c> list of an RFC 6750 <c>Bearer</c> challenge, honoring
/// RFC 7235 quoted-string commas and escaped quotes.
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

        foreach (var pair in SplitOutsideQuotes(parameters))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = pair[..separatorIndex].Trim();
            var value = Unquote(pair[(separatorIndex + 1)..].Trim());
            if (name.Length > 0)
            {
                result[name] = value;
            }
        }

        return result;
    }

    /// <summary>Splits a comma-separated list on commas that fall outside a double-quoted value.</summary>
    private static IEnumerable<string> SplitOutsideQuotes(string text)
    {
        var start = 0;
        var inQuotes = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' && (index == 0 || text[index - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (character == ',' && !inQuotes)
            {
                yield return text[start..index];
                start = index + 1;
            }
        }

        yield return text[start..];
    }

    /// <summary>Strips a matched pair of surrounding double quotes and unescapes <c>\"</c>, best-effort.</summary>
    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1].Replace("\\\"", "\"") : value;
}
