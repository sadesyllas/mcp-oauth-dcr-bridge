using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;

namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Resolves raw configuration into one validated immutable bridge deployment contract.
/// </summary>
public static class BridgeOptionsFactory
{
    private const string SectionName = "Bridge";
    private const int DefaultDcrBodyBytes = 32 * 1024;
    private const int DefaultTokenBodyBytes = 16 * 1024;

    /// <summary>Creates immutable options from configuration and validates every security boundary.</summary>
    /// <param name="configuration">The composed application configuration.</param>
    /// <param name="isDevelopment">Whether the host is in the explicit local-development environment.</param>
    /// <returns>The validated immutable bridge configuration.</returns>
    /// <exception cref="BridgeConfigurationException">Thrown when configuration fails a startup constraint.</exception>
    public static BridgeOptions Create(IConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetRequiredSection(SectionName);
        var allowHttp = isDevelopment && section.GetValue<bool>("AllowHttpForLocalDevelopment");
        var externalBaseUri = ExternalBaseUri(section, allowHttp);
        var authorizationUri = RequiredUri(section, "Upstream:AuthorizationEndpoint", allowHttp);
        var tokenUri = RequiredUri(section, "Upstream:TokenEndpoint", allowHttp);
        var mcpUri = RequiredUri(section, "Upstream:McpUrl", allowHttp);
        var clientId = RequiredText(section, "Upstream:ClientId");
        var redirects = ExactUris(section.GetSection("AllowedRedirectUris"), "AllowedRedirectUris", allowHttp);
        var scopes = ScopeSet(section.GetSection("AllowedScopes"));
        var authentication = Authentication(section.GetSection("Upstream:ClientAuthentication"));
        var headers = HeaderSet(section.GetSection("Upstream:McpHeaders"));

        return new BridgeOptions
        {
            ExternalBaseUri = externalBaseUri,
            UpstreamAuthorizationEndpoint = authorizationUri,
            UpstreamTokenEndpoint = tokenUri,
            UpstreamMcpUri = mcpUri,
            ClientId = clientId,
            AllowedRedirectUris = redirects,
            AllowedScopes = scopes,
            ClientAuthentication = authentication,
            UpstreamMcpHeaders = headers,
            Limits = Limits(section.GetSection("Limits")),
        };
    }

    private static UpstreamClientAuthenticationOptions Authentication(IConfigurationSection section)
    {
        var methodText = RequiredText(section, "Method");
        if (!TryParseAuthenticationMethod(methodText, out var method))
        {
            throw Invalid("Upstream:ClientAuthentication:Method", "must be none, client_secret_post, client_secret_basic, or private_key_jwt");
        }

        var secret = section["ClientSecret"];
        var certificatePath = section["CertificatePath"];
        var needsSecret = method is UpstreamClientAuthenticationMethod.ClientSecretPost or UpstreamClientAuthenticationMethod.ClientSecretBasic;
        if (needsSecret != !string.IsNullOrWhiteSpace(secret) || (!needsSecret && !string.IsNullOrWhiteSpace(secret)))
        {
            throw Invalid("Upstream:ClientAuthentication", "has an inconsistent client secret setting");
        }

        var needsCertificate = method == UpstreamClientAuthenticationMethod.PrivateKeyJwt;
        if (needsCertificate != !string.IsNullOrWhiteSpace(certificatePath) || (!needsCertificate && !string.IsNullOrWhiteSpace(certificatePath)))
        {
            throw Invalid("Upstream:ClientAuthentication", "has an inconsistent certificate setting");
        }

        return new UpstreamClientAuthenticationOptions { Method = method, ClientSecret = secret, CertificatePath = certificatePath };
    }

    private static ImmutableHashSet<string> ExactUris(IConfigurationSection section, string key, bool allowHttp)
    {
        var values = section.Get<string[]>() ?? [];
        if (values.Length == 0)
        {
            throw Invalid(key, "must contain at least one exact redirect URI");
        }

        var result = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var uri = ParseUri(value, key, allowHttp);
            if (!string.IsNullOrEmpty(uri.Fragment) || !result.Add(uri.AbsoluteUri))
            {
                throw Invalid(key, "contains a fragment or duplicate URI");
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableHashSet<string> ScopeSet(IConfigurationSection section)
    {
        var result = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var scope in section.Get<string[]>() ?? [])
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsWhiteSpace) || !result.Add(scope))
            {
                throw Invalid("AllowedScopes", "contains an empty, whitespace-containing, or duplicate scope token");
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> HeaderSet(IConfigurationSection section)
    {
        var result = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in section.GetChildren())
        {
            var name = RequiredText(header, "Name");
            if (!IsHeaderName(name) || ForbiddenUpstreamMcpHeaders.Contains(name))
            {
                throw Invalid($"Upstream:McpHeaders:{header.Key}:Name", "is not permitted");
            }

            var values = header.GetSection("Values").Get<string[]>() ?? [];
            if (values.Length == 0 || values.Any(string.IsNullOrEmpty) || !result.TryAdd(name, values.ToImmutableArray()))
            {
                throw Invalid($"Upstream:McpHeaders:{header.Key}", "must have nonempty values and a unique name");
            }
        }

        return result.ToImmutable();
    }

    private static BridgeLimits Limits(IConfigurationSection section)
    {
        var dcrBytes = Number(section, "DcrRequestBodyBytes", DefaultDcrBodyBytes, 1024, 1024 * 1024);
        var tokenBytes = Number(section, "TokenRequestBodyBytes", DefaultTokenBodyBytes, 1024, 1024 * 1024);
        var permitLimit = Number(section, "RateLimitPermitLimit", 100, 1, 10000);
        return new BridgeLimits
        {
            DcrRequestBodyBytes = dcrBytes,
            TokenRequestBodyBytes = tokenBytes,
            OAuthTimeout = Duration(section, "OAuthTimeoutSeconds", 30, 1, 120),
            McpActivityTimeout = Duration(section, "McpActivityTimeoutSeconds", 300, 1, 3600),
            ShutdownDrainTimeout = Duration(section, "ShutdownDrainTimeoutSeconds", 30, 1, 300),
            RateLimitPermitLimit = permitLimit,
            RateLimitWindow = Duration(section, "RateLimitWindowSeconds", 60, 1, 3600),
        };
    }

    private static int Number(IConfigurationSection section, string key, int defaultValue, int min, int max)
    {
        var raw = section[key];
        if (raw is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var value) || value < min || value > max)
        {
            throw Invalid($"Limits:{key}", $"must be an integer from {min} through {max}");
        }

        return value;
    }

    private static TimeSpan Duration(IConfigurationSection section, string key, int defaultSeconds, int minSeconds, int maxSeconds) =>
        TimeSpan.FromSeconds(Number(section, key, defaultSeconds, minSeconds, maxSeconds));

    private static Uri ExternalBaseUri(IConfiguration section, bool allowHttp)
    {
        var uri = ParseUri(RequiredText(section, "ExternalBaseUrl"), "ExternalBaseUrl", allowHttp);
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static Uri RequiredUri(IConfiguration section, string key, bool allowHttp) => ParseUri(RequiredText(section, key), key, allowHttp);

    private static Uri ParseUri(string? value, string key, bool allowHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp)))
        {
            throw Invalid(key, "must be an absolute HTTPS URI without credentials, query, or fragment");
        }

        return uri;
    }

    private static string RequiredText(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value) ? value : throw Invalid(key, "is required");
    }

    private static bool IsHeaderName(string name) => name.Length > 0 && name.All(character => character is >= '!' and <= '~' && character != ':');

    private static bool TryParseAuthenticationMethod(string value, out UpstreamClientAuthenticationMethod method)
    {
        method = value.ToLowerInvariant() switch
        {
            "none" => UpstreamClientAuthenticationMethod.None,
            "client_secret_post" => UpstreamClientAuthenticationMethod.ClientSecretPost,
            "client_secret_basic" => UpstreamClientAuthenticationMethod.ClientSecretBasic,
            "private_key_jwt" => UpstreamClientAuthenticationMethod.PrivateKeyJwt,
            _ => default,
        };
        return value is "none" or "client_secret_post" or "client_secret_basic" or "private_key_jwt";
    }

    private static BridgeConfigurationException Invalid(string key, string reason) => new($"{SectionName}:{key}", reason);
}
