using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace McpOAuthDcrBridge.TestSupport;

/// <summary>
/// Parses and independently verifies compact JWS strings produced by
/// <c>PrivateKeyJwtAssertionGenerator</c>, without depending on any JWT library. Linked into every
/// test project so there is one authoritative copy.
/// </summary>
internal static class JwsAssertion
{
    /// <summary>Splits a compact JWS into its decoded header, decoded payload, signing input, and raw signature.</summary>
    public static (JsonElement Header, JsonElement Payload, string SigningInput, byte[] Signature) Split(string assertion)
    {
        var parts = assertion.Split('.');
        if (parts.Length != 3)
        {
            throw new ArgumentException("A compact JWS must have exactly three segments.", nameof(assertion));
        }

        var header = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[0]));
        var payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[1]));
        var signature = Base64UrlDecode(parts[2]);
        return (header, payload, $"{parts[0]}.{parts[1]}", signature);
    }

    /// <summary>Independently verifies a compact JWS signature using the certificate's public key.</summary>
    /// <param name="certificate">The certificate whose public key should verify the signature.</param>
    /// <param name="algorithm">The expected JWS algorithm: <c>RS256</c> or <c>ES256</c>.</param>
    /// <param name="assertion">The compact JWS to verify.</param>
    /// <returns><see langword="true"/> when the signature is valid for the certificate's public key.</returns>
    public static bool Verify(X509Certificate2 certificate, string algorithm, string assertion)
    {
        var (_, _, signingInput, signature) = Split(assertion);
        var data = Encoding.ASCII.GetBytes(signingInput);
        switch (algorithm)
        {
            case "RS256":
                using (var rsa = certificate.GetRSAPublicKey() ?? throw new InvalidOperationException("The certificate has no RSA public key."))
                {
                    return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }

            case "ES256":
                using (var ecdsa = certificate.GetECDsaPublicKey() ?? throw new InvalidOperationException("The certificate has no ECDSA public key."))
                {
                    return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
                }

            default:
                throw new NotSupportedException($"Unsupported assertion signing algorithm '{algorithm}'.");
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
