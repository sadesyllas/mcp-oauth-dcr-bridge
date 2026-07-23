namespace McpOAuthDcrBridge.UnitTests.Configuration;

internal static class StringExtensions
{
    public static string ToSnakeCase(this string value) => string.Concat(value.Select((character, index) => char.IsUpper(character) && index > 0 ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}
