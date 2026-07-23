namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Identifies the authentication added by the bridge to the upstream token request.
/// </summary>
public enum UpstreamClientAuthenticationMethod
{
    /// <summary>Adds no upstream client credential.</summary>
    None,

    /// <summary>Adds the configured client secret as a form field.</summary>
    ClientSecretPost,

    /// <summary>Adds the configured client credentials through HTTP Basic authentication.</summary>
    ClientSecretBasic,

    /// <summary>Creates a certificate-backed private key JWT client assertion.</summary>
    PrivateKeyJwt,
}
