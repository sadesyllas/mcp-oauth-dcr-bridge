using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace McpOAuthDcrBridge.Token;

/// <summary>
/// Creates short-lived, uniquely identified RFC 7523 <c>private_key_jwt</c> client assertions signed
/// by the configured upstream certificate's private key. Every call is independent and concurrency
/// safe: it opens a fresh signing key handle and issues a fresh random JWT ID.
/// </summary>
public static class PrivateKeyJwtAssertionGenerator
{
    /// <summary>Creates one signed client assertion for the fixed client, issued for this instant.</summary>
    /// <param name="certificate">The validated signing certificate.</param>
    /// <param name="algorithm">The JWS algorithm implied by the certificate's key, as returned by <see cref="Configuration.PrivateKeyJwtCertificateLoader.SigningAlgorithm"/>.</param>
    /// <param name="clientId">The fixed upstream client ID, used as both issuer and subject.</param>
    /// <param name="audience">The exact upstream token endpoint URI.</param>
    /// <param name="lifetime">The assertion's validity duration from the current instant.</param>
    /// <returns>A compact JWS string: header, payload, and signature.</returns>
    public static string Create(X509Certificate2 certificate, string algorithm, string clientId, string audience, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url(JsonSerializer.Serialize(new { alg = algorithm, typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.Serialize(new
        {
            iss = clientId,
            sub = clientId,
            aud = audience,
            jti = RandomNumberGenerator.GetHexString(32),
            iat = now.ToUnixTimeSeconds(),
            nbf = now.ToUnixTimeSeconds(),
            exp = now.Add(lifetime).ToUnixTimeSeconds(),
        }));
        var unsigned = $"{header}.{payload}";
        var signature = Base64Url(Sign(certificate, algorithm, Encoding.ASCII.GetBytes(unsigned)));
        return $"{unsigned}.{signature}";
    }

    private static byte[] Sign(X509Certificate2 certificate, string algorithm, byte[] data)
    {
        switch (algorithm)
        {
            case "RS256":
                using (var rsa = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("The certificate has no RSA private key."))
                {
                    return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }

            case "ES256":
                using (var ecdsa = certificate.GetECDsaPrivateKey() ?? throw new InvalidOperationException("The certificate has no ECDSA private key."))
                {
                    return ecdsa.SignData(data, HashAlgorithmName.SHA256);
                }

            default:
                throw new NotSupportedException($"Unsupported assertion signing algorithm '{algorithm}'.");
        }
    }

    private static string Base64Url(string text) => Base64Url(Encoding.UTF8.GetBytes(text));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
