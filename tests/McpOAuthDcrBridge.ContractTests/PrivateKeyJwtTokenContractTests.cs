using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Xunit;

namespace McpOAuthDcrBridge.ContractTests;

public sealed class PrivateKeyJwtTokenContractTests
{
    private const string Redirect = "https://client.example.test/callback";
    private const string JwtBearerAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    [Fact]
    public async Task AuthorizationCodeExchangeAddsAFreshVerifiableAssertionAndNeverAClientSecret()
    {
        var certificatePath = TestCertificates.WriteTemporaryPfx(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        using var publicCertificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, string.Empty);
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint, configure: arguments =>
        {
            arguments.Add("--Bridge:Upstream:ClientAuthentication:Method");
            arguments.Add("private_key_jwt");
            arguments.Add("--Bridge:Upstream:ClientAuthentication:CertificatePath");
            arguments.Add(certificatePath);
        });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "fictional-client",
            ["code"] = "auth-code-123",
            ["code_verifier"] = "verifier-abc",
            ["redirect_uri"] = Redirect,
        };
        using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(fakeUpstream.LastForm!.ContainsKey("client_secret"));
        Assert.Equal(JwtBearerAssertionType, fakeUpstream.LastForm!["client_assertion_type"]);
        AssertValidAssertion(fakeUpstream.LastForm!["client_assertion"]!, publicCertificate, fakeUpstream.TokenEndpoint);
        await application.StopAsync();
    }

    [Fact]
    public async Task EachRequestReceivesAFreshAssertionWithAUniqueJwtId()
    {
        var certificatePath = TestCertificates.WriteTemporaryPfx(TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));
        await using var fakeUpstream = await FakeUpstreamTokenServer.StartAsync();
        await using var application = BridgeContractHost.CreateWithUpstreamToken(fakeUpstream.TokenEndpoint, permitLimit: 1000, configure: arguments =>
        {
            arguments.Add("--Bridge:Upstream:ClientAuthentication:Method");
            arguments.Add("private_key_jwt");
            arguments.Add("--Bridge:Upstream:ClientAuthentication:CertificatePath");
            arguments.Add(certificatePath);
        });
        fakeUpstream.OnRequest = context => context.Response.WriteAsJsonAsync(new { access_token = "opaque", client_assertion_echo = context.Request.Form["client_assertion"].ToString() });
        await application.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = "fictional-client", ["refresh_token"] = "abc" };

        var assertions = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using var response = await client.PostAsync("/token", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            assertions.Add(document.RootElement.GetProperty("client_assertion_echo").GetString()!);
        }));

        var jwtIds = assertions.Select(JwtIdOf).ToArray();
        Assert.Equal(20, jwtIds.Length);
        Assert.Equal(jwtIds.Length, jwtIds.Distinct(StringComparer.Ordinal).Count());
        await application.StopAsync();
    }

    private static void AssertValidAssertion(string assertion, X509Certificate2 certificate, string expectedAudience)
    {
        var parts = assertion.Split('.');
        Assert.Equal(3, parts.Length);
        var header = JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement;
        var payload = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
        var signature = Convert.FromBase64String(PadBase64Url(parts[2]));

        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("fictional-client", payload.GetProperty("iss").GetString());
        Assert.Equal("fictional-client", payload.GetProperty("sub").GetString());
        Assert.Equal(expectedAudience, payload.GetProperty("aud").GetString());
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("jti").GetString()));

        using var rsa = certificate.GetRSAPublicKey()!;
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static string JwtIdOf(string assertion)
    {
        var parts = assertion.Split('.');
        var payload = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
        return payload.GetProperty("jti").GetString()!;
    }

    private static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(PadBase64Url(value));

    private static string PadBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
    }
}
