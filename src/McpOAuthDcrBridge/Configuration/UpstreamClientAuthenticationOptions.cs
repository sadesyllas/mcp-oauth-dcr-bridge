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
}
