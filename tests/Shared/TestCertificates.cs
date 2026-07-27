using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace McpOAuthDcrBridge.TestSupport;

/// <summary>
/// Generates self-signed test certificates for exercising private_key_jwt loading and signing
/// without any real key material. Linked into every test project so there is one authoritative copy.
/// </summary>
internal static class TestCertificates
{
    /// <summary>Creates a PKCS#12-encoded self-signed RSA certificate.</summary>
    public static byte[] CreateRsaPfx(DateTimeOffset notBefore, DateTimeOffset notAfter, string password = "", X509KeyUsageFlags? keyUsage = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=bridge-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (keyUsage is { } usage)
        {
            request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        }

        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return certificate.Export(X509ContentType.Pfx, password);
    }

    /// <summary>Creates a PKCS#12-encoded self-signed ECDSA certificate on the named curve size.</summary>
    public static byte[] CreateEcPfx(int keySizeBits, string password = "")
    {
        var curve = keySizeBits switch
        {
            256 => ECCurve.NamedCurves.nistP256,
            384 => ECCurve.NamedCurves.nistP384,
            _ => throw new ArgumentOutOfRangeException(nameof(keySizeBits)),
        };
        using var ecdsa = ECDsa.Create(curve);
        var request = new CertificateRequest("CN=bridge-test-ec", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return certificate.Export(X509ContentType.Pfx, password);
    }

    /// <summary>Creates a PKCS#12-encoded certificate that carries no private key.</summary>
    public static byte[] CreatePublicOnlyPfx(string password = "")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=bridge-test-public-only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        return publicOnly.Export(X509ContentType.Pkcs12, password);
    }

    /// <summary>Writes PKCS#12 bytes to a uniquely named temporary file and returns its path.</summary>
    public static string WriteTemporaryPfx(byte[] pfxBytes, string? fileNameWithoutExtension = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{fileNameWithoutExtension ?? $"bridge-test-{Guid.NewGuid():N}"}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        return path;
    }
}
