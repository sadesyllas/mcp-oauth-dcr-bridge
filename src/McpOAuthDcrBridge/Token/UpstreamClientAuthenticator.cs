using System.Net.Http.Headers;
using System.Text;
using McpOAuthDcrBridge.Configuration;

namespace McpOAuthDcrBridge.Token;

/// <summary>
/// Applies the one configured upstream token-endpoint client authentication method to a forwarded
/// token request. The downstream public client never supplies or observes this credential.
/// </summary>
public static class UpstreamClientAuthenticator
{
    /// <summary>Attaches the configured credential to the outbound token request.</summary>
    /// <param name="request">The outbound request being sent to the fixed upstream token endpoint.</param>
    /// <param name="fields">The form fields already scheduled for forwarding; secret-based methods append to this list.</param>
    /// <param name="options">The validated bridge configuration.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>A task that completes once the required credential, if any, has been attached.</returns>
    public static Task ApplyAsync(HttpRequestMessage request, List<KeyValuePair<string, string>> fields, BridgeOptions options, CancellationToken cancellationToken)
    {
        var authentication = options.ClientAuthentication;
        switch (authentication.Method)
        {
            case UpstreamClientAuthenticationMethod.None:
                break;
            case UpstreamClientAuthenticationMethod.ClientSecretPost:
                fields.Add(new KeyValuePair<string, string>("client_secret", authentication.ClientSecret!));
                break;
            case UpstreamClientAuthenticationMethod.ClientSecretBasic:
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BasicCredential(options.ClientId, authentication.ClientSecret!));
                break;
            case UpstreamClientAuthenticationMethod.PrivateKeyJwt:
                var assertion = PrivateKeyJwtAssertionGenerator.Create(
                    authentication.SigningCertificate!,
                    authentication.SigningAlgorithm!,
                    options.ClientId,
                    options.UpstreamTokenEndpoint.AbsoluteUri,
                    options.Limits.PrivateKeyJwtAssertionLifetime);
                fields.Add(new KeyValuePair<string, string>("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"));
                fields.Add(new KeyValuePair<string, string>("client_assertion", assertion));
                break;
            default:
                throw new NotSupportedException($"Unknown upstream client authentication method '{authentication.Method}'.");
        }

        return Task.CompletedTask;
    }

    private static string BasicCredential(string clientId, string clientSecret) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}"));
}
