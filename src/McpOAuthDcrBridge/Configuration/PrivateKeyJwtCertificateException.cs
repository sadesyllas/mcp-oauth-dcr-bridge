namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Thrown when the configured <c>private_key_jwt</c> certificate or private key cannot be loaded or
/// does not meet the bridge's signing requirements. The message never includes key material.
/// </summary>
public sealed class PrivateKeyJwtCertificateException : Exception
{
    /// <summary>Initializes a new instance with a bounded, non-sensitive reason.</summary>
    /// <param name="message">A bounded description of why the certificate was rejected.</param>
    public PrivateKeyJwtCertificateException(string message) : base(message)
    {
    }
}
