using System.Text.Json;
using System.Collections.Immutable;
using McpOAuthDcrBridge.Configuration;

namespace McpOAuthDcrBridge.Registration;

/// <summary>
/// Maps the deterministic, storage-free public-client dynamic registration endpoint.
/// </summary>
public static class RegistrationEndpointExtensions
{
    private static readonly ImmutableHashSet<string> RejectedFields = ImmutableHashSet.Create(StringComparer.Ordinal, "client_secret", "client_secret_expires_at", "jwks", "jwks_uri", "software_statement", "software_id", "software_version");
    private static readonly ImmutableArray<string> SupportedGrants = ImmutableArray.Create("authorization_code", "refresh_token");
    private static readonly ImmutableArray<string> SupportedResponseTypes = ImmutableArray.Create("code");
    /// <summary>Maps the bridge's fixed-client registration endpoint.</summary>
    /// <param name="application">The application endpoint route builder.</param>
    /// <returns>The same application for composition.</returns>
    public static WebApplication MapRegistrationEndpoint(this WebApplication application, BridgeOptions options)
    {
        application.MapPost("/register", (Func<HttpContext, Task<IResult>>)(context => RegisterAsync(context, options))).RequireRateLimiting("dcr");
        return application;
    }

    private static async Task<IResult> RegisterAsync(HttpContext context, BridgeOptions options)
    {
        if (!context.Request.HasJsonContentType()) return Error();
        var bytes = await ReadBodyAsync(context.Request, options.Limits.DcrRequestBodyBytes, context.RequestAborted);
        if (bytes is null) return Error();
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return Validate(document.RootElement, options);
        }
        catch (JsonException)
        {
            return Error();
        }
    }

    private static IResult Validate(JsonElement metadata, BridgeOptions options)
    {
        if (metadata.ValueKind != JsonValueKind.Object || HasDuplicateProperties(metadata) || HasRejectedField(metadata) || !Strings(metadata, "redirect_uris", required: true, out var redirects) || redirects.Count == 0 || redirects.Distinct(StringComparer.Ordinal).Count() != redirects.Count) return Error();
        if (redirects.Any(redirect => !options.AllowedRedirectUris.Contains(redirect))) return Error("invalid_redirect_uri");
        if (!Strings(metadata, "response_types", required: false, out var responseTypes) || (metadata.TryGetProperty("response_types", out _) && responseTypes.Count == 0) || responseTypes.Distinct(StringComparer.Ordinal).Count() != responseTypes.Count || responseTypes.Any(type => type != "code")) return Error();
        if (!Strings(metadata, "grant_types", required: false, out var grantTypes) || (metadata.TryGetProperty("grant_types", out _) && grantTypes.Count == 0) || grantTypes.Distinct(StringComparer.Ordinal).Count() != grantTypes.Count || grantTypes.Any(grant => !SupportedGrants.Contains(grant))) return Error();
        if (metadata.TryGetProperty("token_endpoint_auth_method", out var authMethod) && (authMethod.ValueKind != JsonValueKind.String || authMethod.GetString() != "none")) return Error();
        string? scope = null;
        if (metadata.TryGetProperty("scope", out var scopeValue))
        {
            if (scopeValue.ValueKind != JsonValueKind.String) return Error();
            scope = scopeValue.GetString();
            if (scope is null || !ScopeAllowed(scope, options.AllowedScopes)) return Error();
        }

        var response = new Dictionary<string, object>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uris"] = redirects,
            ["response_types"] = SupportedResponseTypes,
            ["grant_types"] = SupportedGrants,
            ["token_endpoint_auth_method"] = "none",
        };
        if (scope is not null) response["scope"] = scope;
        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static bool HasRejectedField(JsonElement metadata) => RejectedFields.Any(field => metadata.TryGetProperty(field, out _));

    private static bool ScopeAllowed(string scope, ImmutableHashSet<string> allowedScopes)
    {
        var tokens = scope.Split(' ', StringSplitOptions.None);
        return tokens.Length > 0 && tokens.All(OAuthScopeToken.IsValid) && (allowedScopes.Count == 0 || tokens.All(token => allowedScopes.Contains(token)));
    }

    private static bool HasDuplicateProperties(JsonElement metadata) => metadata.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private static bool Strings(JsonElement metadata, string name, bool required, out List<string> values)
    {
        values = [];
        if (!metadata.TryGetProperty(name, out var element)) return !required;
        if (element.ValueKind != JsonValueKind.Array) return false;
        foreach (var value in element.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text) return false;
            values.Add(text);
        }

        return true;
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > 0 and var length && length > maximumBytes) return null;
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximumBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static IResult Error(string code = "invalid_client_metadata") => Results.Json(new { error = code, error_description = "invalid client metadata" }, statusCode: StatusCodes.Status400BadRequest);
}
