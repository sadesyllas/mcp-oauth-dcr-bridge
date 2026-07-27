using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace McpOAuthDcrBridge.ContractTests;

internal static class TestCertificates
{
    public static byte[] CreateRsaPfx(DateTimeOffset notBefore, DateTimeOffset notAfter, string password = "")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=bridge-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return certificate.Export(X509ContentType.Pfx, password);
    }

    public static string WriteTemporaryPfx(byte[] pfxBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bridge-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        return path;
    }
}
