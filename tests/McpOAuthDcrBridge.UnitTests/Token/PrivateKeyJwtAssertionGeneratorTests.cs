using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.Token;
using McpOAuthDcrBridge.UnitTests.Configuration;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Token;

public sealed class PrivateKeyJwtAssertionGeneratorTests
{
    private const string ClientId = "fictional-client";
    private const string Audience = "https://login.example.test/token";

    [Fact]
    public void CreateProducesAnRs256AssertionWithAnIndependentlyVerifiableSignature()
    {
        using var certificate = LoadCertificate(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        var assertion = PrivateKeyJwtAssertionGenerator.Create(certificate, "RS256", ClientId, Audience, TimeSpan.FromSeconds(60));
        var (header, payload, signingInput, signature) = Split(assertion);

        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
        using var rsa = certificate.GetRSAPublicKey()!;
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        AssertClaims(payload, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void CreateProducesAnEs256AssertionWithAnIndependentlyVerifiableSignature()
    {
        using var certificate = LoadCertificate(TestCertificates.CreateEcPfx(256));
        var assertion = PrivateKeyJwtAssertionGenerator.Create(certificate, "ES256", ClientId, Audience, TimeSpan.FromSeconds(60));
        var (header, payload, signingInput, signature) = Split(assertion);

        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        using var ecdsa = certificate.GetECDsaPublicKey()!;
        Assert.True(ecdsa.VerifyData(Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256));
        AssertClaims(payload, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void CreateHonorsTheConfiguredLifetimeExactly()
    {
        using var certificate = LoadCertificate(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        var assertion = PrivateKeyJwtAssertionGenerator.Create(certificate, "RS256", ClientId, Audience, TimeSpan.FromSeconds(37));
        var (_, payload, _, _) = Split(assertion);

        Assert.Equal(37, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
    }

    [Fact]
    public void CreateUsesTheExactAudienceAndClientIdSupplied()
    {
        using var certificate = LoadCertificate(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        var assertion = PrivateKeyJwtAssertionGenerator.Create(certificate, "RS256", "other-client", "https://other.example.test/token", TimeSpan.FromSeconds(60));
        var (_, payload, _, _) = Split(assertion);

        Assert.Equal("other-client", payload.GetProperty("iss").GetString());
        Assert.Equal("other-client", payload.GetProperty("sub").GetString());
        Assert.Equal("https://other.example.test/token", payload.GetProperty("aud").GetString());
    }

    [Fact]
    public void CreateGeneratesAFreshUniqueJwtIdOnEveryCall()
    {
        using var certificate = LoadCertificate(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));

        var jwtIds = Enumerable.Range(0, 200)
            .Select(_ => Split(PrivateKeyJwtAssertionGenerator.Create(certificate, "RS256", ClientId, Audience, TimeSpan.FromSeconds(60))).Payload.GetProperty("jti").GetString())
            .ToArray();

        Assert.Equal(jwtIds.Length, jwtIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task CreateIsConcurrencySafeAndEveryConcurrentAssertionHasAUniqueJwtId()
    {
        using var certificate = LoadCertificate(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() =>
            PrivateKeyJwtAssertionGenerator.Create(certificate, "RS256", ClientId, Audience, TimeSpan.FromSeconds(60)))));

        var jwtIds = results.Select(assertion => Split(assertion).Payload.GetProperty("jti").GetString()).ToArray();
        Assert.Equal(jwtIds.Length, jwtIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(results, assertion => Assert.True(VerifiesWith(certificate, "RS256", assertion)));
    }

    private static void AssertClaims(JsonElement payload, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.Equal(ClientId, payload.GetProperty("iss").GetString());
        Assert.Equal(ClientId, payload.GetProperty("sub").GetString());
        Assert.Equal(Audience, payload.GetProperty("aud").GetString());
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("jti").GetString()));
        Assert.InRange(payload.GetProperty("iat").GetInt64(), now - 5, now + 5);
        Assert.InRange(payload.GetProperty("nbf").GetInt64(), now - 5, now + 5);
        Assert.Equal(payload.GetProperty("iat").GetInt64() + (long)lifetime.TotalSeconds, payload.GetProperty("exp").GetInt64());
    }

    private static bool VerifiesWith(X509Certificate2 certificate, string algorithm, string assertion)
    {
        var (_, _, signingInput, signature) = Split(assertion);
        if (algorithm == "RS256")
        {
            using var rsa = certificate.GetRSAPublicKey()!;
            return rsa.VerifyData(Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        using var ecdsa = certificate.GetECDsaPublicKey()!;
        return ecdsa.VerifyData(Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256);
    }

    private static X509Certificate2 LoadCertificate(byte[] pfxBytes) => PrivateKeyJwtCertificateLoader.Load(pfxBytes, string.Empty);

    private static (JsonElement Header, JsonElement Payload, string SigningInput, byte[] Signature) Split(string assertion)
    {
        var parts = assertion.Split('.');
        Assert.Equal(3, parts.Length);
        var header = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[0]));
        var payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[1]));
        var signature = Convert.FromBase64String(PadBase64Url(parts[2]));
        return (header, payload, $"{parts[0]}.{parts[1]}", signature);
    }

    private static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(PadBase64Url(value));

    private static string PadBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
    }
}
