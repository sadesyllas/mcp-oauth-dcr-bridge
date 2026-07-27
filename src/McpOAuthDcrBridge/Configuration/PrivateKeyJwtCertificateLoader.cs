using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Loads and validates the PKCS#12 certificate and private key configured for
/// <c>private_key_jwt</c> upstream client authentication. The private key is kept in process memory
/// only; nothing is persisted to a machine or user certificate store.
/// </summary>
public static class PrivateKeyJwtCertificateLoader
{
    /// <summary>Loads and validates a certificate from a PKCS#12 (<c>.pfx</c>) file.</summary>
    /// <param name="certificatePath">The filesystem path to the PKCS#12 file.</param>
    /// <param name="password">The optional PKCS#12 password.</param>
    /// <returns>A validated certificate with an accessible private key.</returns>
    /// <exception cref="PrivateKeyJwtCertificateException">The file, certificate, or key is unusable.</exception>
    public static X509Certificate2 LoadFromFile(string certificatePath, string? password)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(certificatePath);
        }
        catch (IOException)
        {
            throw new PrivateKeyJwtCertificateException("the certificate file could not be read");
        }
        catch (UnauthorizedAccessException)
        {
            throw new PrivateKeyJwtCertificateException("the certificate file could not be read");
        }

        return Load(bytes, password);
    }

    /// <summary>Loads and validates a certificate from PKCS#12 (<c>.pfx</c>) bytes.</summary>
    /// <param name="pkcs12Bytes">The raw PKCS#12 file contents.</param>
    /// <param name="password">The optional PKCS#12 password.</param>
    /// <returns>A validated certificate with an accessible private key.</returns>
    /// <exception cref="PrivateKeyJwtCertificateException">The certificate or key is unusable.</exception>
    public static X509Certificate2 Load(byte[] pkcs12Bytes, string? password)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(pkcs12Bytes, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            throw new PrivateKeyJwtCertificateException("the certificate file is missing, corrupted, or has an incorrect password");
        }

        try
        {
            Validate(certificate);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }

        return certificate;
    }

    /// <summary>Determines the JWS signing algorithm implied by a certificate's private key, if supported.</summary>
    /// <param name="certificate">A certificate with an accessible private key.</param>
    /// <returns><c>RS256</c> for RSA keys, <c>ES256</c> for P-256 ECDSA keys, or <see langword="null"/> when unsupported.</returns>
    public static string? SigningAlgorithm(X509Certificate2 certificate)
    {
        using (var rsa = certificate.GetRSAPrivateKey())
        {
            if (rsa is not null) return "RS256";
        }

        using (var ecdsa = certificate.GetECDsaPrivateKey())
        {
            if (ecdsa is { KeySize: 256 }) return "ES256";
        }

        return null;
    }

    private static void Validate(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new PrivateKeyJwtCertificateException("the certificate has no private key");
        }

        var now = DateTimeOffset.UtcNow;
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
        {
            throw new PrivateKeyJwtCertificateException("the certificate is not currently valid");
        }

        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage is not null && !keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
        {
            throw new PrivateKeyJwtCertificateException("the certificate key usage does not permit digital signatures");
        }

        if (SigningAlgorithm(certificate) is null)
        {
            throw new PrivateKeyJwtCertificateException("the certificate key algorithm is not supported");
        }
    }
}
