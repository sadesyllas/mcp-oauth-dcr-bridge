using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

internal static class TestCertificates
{
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

    public static string WriteTemporaryPfx(byte[] pfxBytes, string? fileNameWithoutExtension = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{fileNameWithoutExtension ?? $"bridge-test-{Guid.NewGuid():N}"}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        return path;
    }
}
