using System.Security.Cryptography.X509Certificates;
using McpOAuthDcrBridge.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Configuration;

public sealed class PrivateKeyJwtCertificateLoaderTests
{
    [Fact]
    public void LoadAcceptsAValidRsaCertificateAndReportsRs256()
    {
        var bytes = TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        using var certificate = PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty);

        Assert.True(certificate.HasPrivateKey);
        Assert.Equal("RS256", PrivateKeyJwtCertificateLoader.SigningAlgorithm(certificate));
    }

    [Fact]
    public void LoadAcceptsAValidP256EcdsaCertificateAndReportsEs256()
    {
        var bytes = TestCertificates.CreateEcPfx(256);

        using var certificate = PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty);

        Assert.Equal("ES256", PrivateKeyJwtCertificateLoader.SigningAlgorithm(certificate));
    }

    [Fact]
    public void LoadAcceptsAPasswordProtectedCertificate()
    {
        var bytes = TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), password: "correct-password");

        using var certificate = PrivateKeyJwtCertificateLoader.Load(bytes, "correct-password");

        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    public void LoadRejectsAnIncorrectPassword()
    {
        var bytes = TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), password: "correct-password");

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(bytes, "wrong-password"));
    }

    [Fact]
    public void LoadRejectsCorruptedMaterial()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(garbage, string.Empty));
    }

    [Fact]
    public void LoadRejectsACertificateWithoutAPrivateKey()
    {
        var bytes = TestCertificates.CreatePublicOnlyPfx();

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty));
    }

    [Fact]
    public void LoadRejectsAnExpiredCertificate()
    {
        var bytes = TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty));
    }

    [Fact]
    public void LoadRejectsANotYetValidCertificate()
    {
        var bytes = TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(30));

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty));
    }

    [Fact]
    public void LoadRejectsAKeyUsageExtensionThatExcludesDigitalSignatures()
    {
        var bytes = TestCertificates.CreateRsaPfx(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            keyUsage: X509KeyUsageFlags.KeyEncipherment);

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty));
    }

    [Fact]
    public void LoadAcceptsAKeyUsageExtensionThatIncludesDigitalSignatures()
    {
        var bytes = TestCertificates.CreateRsaPfx(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            keyUsage: X509KeyUsageFlags.DigitalSignature);

        using var certificate = PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty);

        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    public void LoadRejectsAnUnsupportedEcdsaCurve()
    {
        var bytes = TestCertificates.CreateEcPfx(384);

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(bytes, string.Empty));
    }

    [Fact]
    public void LoadFromFileRejectsAMissingFile()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pfx");

        Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.LoadFromFile(missingPath, null));
    }

    [Fact]
    public void ExceptionMessagesNeverIncludeCertificateBytesOrPassword()
    {
        const string passwordCanary = "certificate-password-canary-6a41c";
        var bytes = TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), password: passwordCanary);

        var exception = Assert.Throws<PrivateKeyJwtCertificateException>(() => PrivateKeyJwtCertificateLoader.Load(bytes, "wrong-password"));

        Assert.DoesNotContain(passwordCanary, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(passwordCanary, exception.ToString(), StringComparison.Ordinal);
    }
}
