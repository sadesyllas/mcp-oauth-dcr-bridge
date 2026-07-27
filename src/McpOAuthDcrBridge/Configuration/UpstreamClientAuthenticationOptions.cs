using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Contains the validated credential material selector for the fixed upstream client.
/// </summary>
public sealed class UpstreamClientAuthenticationOptions
{
    /// <summary>Gets the selected upstream authentication method.</summary>
    public required UpstreamClientAuthenticationMethod Method { get; init; }

    /// <summary>Gets the client secret when a secret-based method is selected. It is excluded from JSON diagnostics.</summary>
    [JsonIgnore]
    public string? ClientSecret { get; init; }

    /// <summary>Gets the certificate path when private key JWT is selected. It is excluded from JSON diagnostics.</summary>
    [JsonIgnore]
    public string? CertificatePath { get; init; }

    /// <summary>Gets the loaded, validated signing certificate when private key JWT is selected. It is excluded from JSON diagnostics because it carries the private key.</summary>
    [JsonIgnore]
    public X509Certificate2? SigningCertificate { get; init; }

    /// <summary>Gets the JWS algorithm implied by <see cref="SigningCertificate"/>'s key, when private key JWT is selected.</summary>
    public string? SigningAlgorithm { get; init; }
}
