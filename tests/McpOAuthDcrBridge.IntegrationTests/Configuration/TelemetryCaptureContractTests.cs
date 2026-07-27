using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace McpOAuthDcrBridge.IntegrationTests.Configuration;

public sealed partial class TelemetryCaptureContractTests
{
    private static readonly string[] MetricStatusClasses = ["2xx", "3xx", "4xx", "5xx"];

    [Fact]
    public async Task SharedCaptureHarnessLocksM2TelemetryAndM4RegistrationCanaryContracts()
    {
        var certificateCanaryPath = TestCertificates.WriteTemporaryPfx(
            TestCertificates.CreateRsaPfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)),
            "certificate-canary-9bc44");
        var canaries = new TestCanaries(
            "configured-secret-canary-1fa31", "registration-secret-canary-2b940", "invalid-redirect-canary-4c882",
            "unsupported-scope-canary-8a9e7", "authorization-canary-5ca22", "oauth-query-canary-70bd1",
            "cookie-canary-10a2f", "exception-canary-65cb4", certificateCanaryPath,
            "configured-header-canary-c1c71", "response-canary-20a8b", "custom-header-canary-3f173",
            "authorize-challenge-canary-9e203", "authorize-state-canary-7d51f", "authorize-scope-canary-4b8a6",
            "token-code-canary-f21ac", "token-verifier-canary-6d905", "token-refresh-canary-b47e2");
        using var capture = new TelemetryCapture();
        var arguments = ValidBridgeCommandLine.Arguments.Concat([
            "--Bridge:AllowedScopes:0", "mcp.read",
            "--Bridge:Upstream:ClientAuthentication:Method", "client_secret_post",
            "--Bridge:Upstream:ClientAuthentication:ClientSecret", canaries.ConfiguredSecret,
            "--Bridge:Upstream:McpHeaders:0:Name", "X-Configured",
            "--Bridge:Upstream:McpHeaders:0:Values:0", canaries.ConfiguredHeader,
        ]).ToArray();
        Assert.Contains(canaries.ConfiguredSecret, arguments);
        Assert.Contains(canaries.ConfiguredHeader, arguments);
        await using var application = McpOAuthDcrBridge.BridgeApplication.Build(arguments, null, logging => logging.AddProvider(capture.LoggerProvider));
        var observedException = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Exception> exceptionFactory = () => new InvalidOperationException(canaries.Exception);
        var responseBody = canaries.Response;
        application.MapGet("/test-throw", (HttpContext _) => ThrowObservedException(exceptionFactory, observedException));
        application.MapGet("/test-response", () => Results.Text(responseBody));
        application.MapGet("/test-rejected-log", (ILoggerFactory factory) =>
        {
            LogRejectedCategory(factory.CreateLogger("Framework.Future.Category"), canaries.Authorization);
            return Results.NoContent();
        });
        await application.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(application.Urls.Single()) };

        var registrationPath = $"/register?client_id={canaries.Query}&redirect_uri={canaries.InvalidRedirect}";
        var authorizeValidPath = $"/authorize?client_id=fictional-client&redirect_uri={Uri.EscapeDataString("https://client.example.test/callback")}&response_type=code&code_challenge={Uri.EscapeDataString(canaries.AuthorizationChallenge)}&code_challenge_method=S256&scope=mcp.read&state={Uri.EscapeDataString(canaries.AuthorizationState)}";
        var authorizeScopeRejectedPath = $"/authorize?client_id=fictional-client&redirect_uri={Uri.EscapeDataString("https://client.example.test/callback")}&response_type=code&code_challenge=challenge&code_challenge_method=S256&scope={Uri.EscapeDataString(canaries.AuthorizationScope)}";
        var authorizeInvalidRedirectPath = $"/authorize?client_id=fictional-client&redirect_uri={Uri.EscapeDataString($"https://client.example.test/{canaries.InvalidRedirect}")}&response_type=code&code_challenge=challenge&code_challenge_method=S256";
        using var authorizeValidRequest = new HttpRequestMessage(HttpMethod.Get, authorizeValidPath);
        using var authorizeScopeRejectedRequest = new HttpRequestMessage(HttpMethod.Get, authorizeScopeRejectedPath);
        using var authorizeInvalidRedirectRequest = new HttpRequestMessage(HttpMethod.Get, authorizeInvalidRedirectPath);
        var tokenAuthorizationCodeBody = $"grant_type=authorization_code&client_id=fictional-client&code={Uri.EscapeDataString(canaries.TokenCode)}&code_verifier={Uri.EscapeDataString(canaries.TokenVerifier)}&redirect_uri={Uri.EscapeDataString("https://client.example.test/callback")}";
        var tokenRefreshBody = $"grant_type=refresh_token&client_id=fictional-client&refresh_token={Uri.EscapeDataString(canaries.TokenRefreshToken)}";
        using var tokenAuthorizationCodeRequest = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = new StringContent(tokenAuthorizationCodeBody, Encoding.UTF8, "application/x-www-form-urlencoded") };
        using var tokenRefreshRequest = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = new StringContent(tokenRefreshBody, Encoding.UTF8, "application/x-www-form-urlencoded") };
        var authorizationHeader = $"Bearer {canaries.Authorization}";
        var cookieHeader = $"session={canaries.Cookie}";
        var customHeader = canaries.CustomHeader;
        var registrationCases = new[]
        {
            $"{{\"redirect_uris\":[\"https://client.example.test/callback\"],\"client_secret\":\"{canaries.RegistrationSecret}\"}}",
            $"{{\"redirect_uris\":[\"https://client.example.test/{canaries.InvalidRedirect}\"]}}",
            $"{{\"redirect_uris\":[\"https://client.example.test/callback\"],\"scope\":\"{canaries.UnsupportedScope}\"}}",
        };
        using var credentialRequest = CreateRegistrationRequest(registrationPath, registrationCases[0], authorizationHeader, cookieHeader, customHeader);
        using var redirectRequest = CreateRegistrationRequest(registrationPath, registrationCases[1], authorizationHeader, cookieHeader, customHeader);
        using var scopeRequest = CreateRegistrationRequest(registrationPath, registrationCases[2], authorizationHeader, cookieHeader, customHeader);
        HttpRequestMessage[] registrationRequests = [credentialRequest, redirectRequest, scopeRequest];
        var privateKeyJwtArguments = ValidBridgeCommandLine.Create("private_key_jwt", certificatePath: canaries.CertificatePath);
        AssertInputSurfaces(
            canaries,
            await CreateInputSurfacesAsync(
                arguments,
                registrationRequests,
                authorizeValidRequest,
                authorizeScopeRejectedRequest,
                tokenAuthorizationCodeRequest,
                tokenRefreshRequest,
                exceptionFactory,
                responseBody,
                privateKeyJwtArguments));
        var registrationArtifacts = new List<CapturedResponse>();
        foreach (var request in registrationRequests)
        {
            using var response = await client.SendAsync(request);
            registrationArtifacts.Add(await CaptureResponseAsync(response));
        }
        var registrationLogs = capture.Logs.ToArray();
        var registrationActivities = capture.Activities.ToArray();
        var registrationMeasurements = capture.Measurements.ToArray();
        using var exceptionHttpResponse = await client.GetAsync($"/test-throw?code={canaries.Query}");
        var exceptionResponse = await CaptureResponseAsync(exceptionHttpResponse);
        using var responseCanaryHttpResponse = await client.GetAsync("/test-response");
        var responseCanaryResponse = await CaptureResponseAsync(responseCanaryHttpResponse);
        using var rejectedLogHttpResponse = await client.GetAsync($"/test-rejected-log?code={canaries.Query}");
        var rejectedLogResponse = await CaptureResponseAsync(rejectedLogHttpResponse);
        using var authorizeValidHttpResponse = await client.SendAsync(authorizeValidRequest);
        var authorizeValidResponse = await CaptureResponseAsync(authorizeValidHttpResponse);
        using var authorizeScopeRejectedHttpResponse = await client.SendAsync(authorizeScopeRejectedRequest);
        var authorizeScopeRejectedResponse = await CaptureResponseAsync(authorizeScopeRejectedHttpResponse);
        using var authorizeInvalidRedirectHttpResponse = await client.SendAsync(authorizeInvalidRedirectRequest);
        var authorizeInvalidRedirectResponse = await CaptureResponseAsync(authorizeInvalidRedirectHttpResponse);
        using var tokenAuthorizationCodeHttpResponse = await client.SendAsync(tokenAuthorizationCodeRequest);
        var tokenAuthorizationCodeResponse = await CaptureResponseAsync(tokenAuthorizationCodeHttpResponse);
        using var tokenRefreshHttpResponse = await client.SendAsync(tokenRefreshRequest);
        var tokenRefreshResponse = await CaptureResponseAsync(tokenRefreshHttpResponse);
        var healthArtifacts = new[] { await CaptureResponseAsync(client, "/health/live"), await CaptureResponseAsync(client, "/health/ready") };
        for (var index = 0; index < 100; index++)
        {
            using var hostile = new HttpRequestMessage((index % 4) switch { 0 => HttpMethod.Get, 1 => HttpMethod.Post, 2 => HttpMethod.Delete, _ => HttpMethod.Patch }, $"/hostile-{index}?input={canaries.Authorization}-{index}");
            hostile.Headers.Host = $"host-{index}.example.test";
            hostile.Headers.TryAddWithoutValidation("X-Forwarded-Host", $"forwarded-{index}.example.test");
            hostile.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
            hostile.Headers.TryAddWithoutValidation("Forwarded", $"host=forwarded-{index}.example.test;proto=http");
            hostile.Headers.TryAddWithoutValidation("X-Correlation-ID", $"invalid correlation {index} {canaries.Query}");
            hostile.Headers.TryAddWithoutValidation("X-Custom-Canary", canaries.CustomHeader);
            using var _ = await client.SendAsync(hostile);
        }

        AssertRegistrationError(registrationArtifacts[0], "invalid_client_metadata");
        AssertRegistrationError(registrationArtifacts[1], "invalid_redirect_uri");
        AssertRegistrationError(registrationArtifacts[2], "invalid_client_metadata");
        Assert.Equal(3, registrationLogs.Length);
        Assert.Equal(3, registrationActivities.Length);
        Assert.Equal(6, registrationMeasurements.Length);
        Assert.All(registrationLogs, entry => AssertRegistrationLog(entry));
        Assert.All(registrationActivities, AssertRegistrationActivity);
        Assert.All(registrationMeasurements, AssertRegistrationMeasurement);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, exceptionResponse.StatusCode);
        Assert.Equal(canaries.Exception, await observedException.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(responseBody, responseCanaryResponse.Body);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, rejectedLogResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Found, authorizeValidResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Found, authorizeScopeRejectedResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, authorizeInvalidRedirectResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadGateway, tokenAuthorizationCodeResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadGateway, tokenRefreshResponse.StatusCode);
        Assert.All(healthArtifacts, artifact =>
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, artifact.StatusCode);
            Assert.Equal("text/plain", artifact.ContentType);
            Assert.Equal("Healthy", artifact.Body);
            Assert.Single(artifact.Headers, header => header.Key == "X-Correlation-ID");
        });
        Assert.NotEmpty(capture.Logs);
        Assert.NotEmpty(capture.Activities);
        Assert.NotEmpty(capture.Measurements);
        Assert.All(capture.Logs, entry =>
        {
            Assert.Equal(typeof(McpOAuthDcrBridge.Telemetry.RequestTelemetryMiddleware).FullName, entry.Category);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal(
                ["CorrelationId", "ElapsedMilliseconds", "Method", "Result", "Route", "StatusClass", "StatusCode", "{OriginalFormat}"],
                entry.State.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(393314459, entry.EventId.Id);
            Assert.Null(entry.Exception);
            Assert.Equal("Bridge request {Method} completed for {Route} with {StatusCode} ({StatusClass}, {Result}) in {ElapsedMilliseconds} ms, correlation {CorrelationId}", entry.State["{OriginalFormat}"]);
        });
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "registration" && entry.State["StatusCode"] == "400" && entry.State["StatusClass"] == "4xx" && entry.State["Result"] == "failure");
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "other" && entry.State["StatusCode"] == "500" && entry.State["StatusClass"] == "5xx" && entry.State["Result"] == "failure");
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "health_live" && entry.State["StatusCode"] == "200" && entry.State["StatusClass"] == "2xx" && entry.State["Result"] == "success");
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "authorization" && entry.State["StatusCode"] == "302" && entry.State["StatusClass"] == "3xx" && entry.State["Result"] == "success");
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "authorization" && entry.State["StatusCode"] == "400" && entry.State["StatusClass"] == "4xx" && entry.State["Result"] == "failure");
        Assert.Contains(capture.Logs, entry => entry.State["Route"] == "token" && entry.State["StatusCode"] == "502" && entry.State["StatusClass"] == "5xx" && entry.State["Result"] == "failure");
        Assert.DoesNotContain(capture.Logs, entry => entry.ToString().Contains(canaries.Exception, StringComparison.Ordinal));
        var requestActivities = capture.Activities.Where(activity => activity.Name == "bridge.request").ToArray();
        var upstreamOAuthActivities = capture.Activities.Where(activity => activity.Name == "bridge.upstream.oauth").ToArray();
        Assert.Contains(requestActivities, activity => activity.Status == ActivityStatusCode.Error && activity.Tags.TryGetValue("bridge.route", out var route) && route == "registration" && activity.Tags.TryGetValue("bridge.result", out var result) && result == "failure");
        Assert.All(requestActivities, activity =>
        {
            Assert.Equal(["bridge.correlation_id", "bridge.method", "bridge.result", "bridge.route", "http.response.status_code"], activity.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.Empty(activity.Events);
            Assert.Empty(activity.Baggage);
        });
        Assert.Equal(2, upstreamOAuthActivities.Length);
        Assert.All(upstreamOAuthActivities, activity =>
        {
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Equal(["bridge.grant", "bridge.result"], activity.Tags.Keys.Order(StringComparer.Ordinal));
            Assert.True(activity.Tags["bridge.grant"] is "authorization_code" or "refresh_token");
            Assert.Equal("error", activity.Tags["bridge.result"]);
            Assert.Empty(activity.Events);
            Assert.Empty(activity.Baggage);
        });
        Assert.Contains(capture.Measurements, measurement => measurement.Name == "bridge.requests" && measurement.Kind == "long");
        Assert.Contains(capture.Measurements, measurement => measurement.Name == "bridge.request.duration" && measurement.Kind == "double");
        Assert.All(capture.Measurements.Where(measurement => measurement.Name is "bridge.requests" or "bridge.request.duration"), measurement => Assert.Equal(["route", "status"], measurement.Tags.Keys.Order(StringComparer.Ordinal)));
        var allowedRoutes = new HashSet<string>(["registration", "authorization", "token", "health_live", "health_ready", "other"], StringComparer.Ordinal);
        Assert.All(capture.Measurements.Where(measurement => measurement.Name is "bridge.requests" or "bridge.request.duration"), measurement =>
        {
            Assert.Contains(measurement.Tags["route"], allowedRoutes);
            Assert.Contains(measurement.Tags["status"], MetricStatusClasses);
        });
        Assert.True(capture.Measurements.Where(measurement => measurement.Name is "bridge.requests" or "bridge.request.duration").Select(measurement => $"{measurement.Name}:{measurement.Tags["route"]}:{measurement.Tags["status"]}").Distinct(StringComparer.Ordinal).Count() <= 28);

        var telemetryArtifacts = FlattenArtifacts(capture.Logs, capture.Activities, capture.Measurements);
        var httpArtifacts = FlattenArtifacts(registrationArtifacts.Concat(healthArtifacts).Concat([exceptionResponse, responseCanaryResponse, rejectedLogResponse, authorizeValidResponse, authorizeScopeRejectedResponse, authorizeInvalidRedirectResponse, tokenAuthorizationCodeResponse, tokenRefreshResponse]));
        AssertCanariesAreAbsent(canaries.All, telemetryArtifacts, canaries.Response);
        AssertCanariesAreAbsent(canaries.All, httpArtifacts, canaries.Response, canaries.AuthorizationChallenge, canaries.AuthorizationState);

        Assert.Contains(canaries.CertificatePath, privateKeyJwtArguments);
        using var privateKeyJwtApplication = McpOAuthDcrBridge.BridgeApplication.Build(privateKeyJwtArguments, null, logging => logging.AddProvider(capture.LoggerProvider));
        await privateKeyJwtApplication.StartAsync();
        using var privateKeyJwtClient = new HttpClient { BaseAddress = new Uri(privateKeyJwtApplication.Urls.Single()) };
        using var privateKeyJwtHealthResponse = await privateKeyJwtClient.GetAsync("/health/ready");
        var privateKeyJwtHealth = await CaptureResponseAsync(privateKeyJwtHealthResponse);
        Assert.Equal(System.Net.HttpStatusCode.OK, privateKeyJwtHealth.StatusCode);
        await privateKeyJwtApplication.StopAsync();
        Assert.DoesNotContain(canaries.CertificatePath, FlattenArtifacts(capture.Logs, capture.Activities, capture.Measurements, [privateKeyJwtHealth]), StringComparison.Ordinal);

        await application.StopAsync();
    }

    private static async Task<CapturedResponse> CaptureResponseAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return await CaptureResponseAsync(response);
    }

    private static async Task<CapturedResponse> CaptureResponseAsync(HttpResponseMessage response) => new(
        response.StatusCode,
        response.Content.Headers.ContentType?.MediaType,
        response.Headers.Concat(response.Content.Headers).ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
        await response.Content.ReadAsStringAsync());

    private static HttpRequestMessage CreateRegistrationRequest(
        string path,
        string json,
        string authorizationHeader,
        string cookieHeader,
        string customHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        request.Headers.TryAddWithoutValidation("X-Custom-Canary", customHeader);
        return request;
    }

    private static async Task<Dictionary<string, string>> CreateInputSurfacesAsync(
        IReadOnlyList<string> arguments,
        HttpRequestMessage[] registrationRequests,
        HttpRequestMessage authorizeValidRequest,
        HttpRequestMessage authorizeScopeRejectedRequest,
        HttpRequestMessage tokenAuthorizationCodeRequest,
        HttpRequestMessage tokenRefreshRequest,
        Func<Exception> exceptionFactory,
        string responseBody,
        IReadOnlyList<string> privateKeyJwtArguments)
    {
        var credentialRequest = registrationRequests[0];
        var redirectRequest = registrationRequests[1];
        var scopeRequest = registrationRequests[2];
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["configured_secret"] = ArgumentValue(arguments, "--Bridge:Upstream:ClientAuthentication:ClientSecret"),
            ["registration_secret"] = JsonPropertyValue(await credentialRequest.Content!.ReadAsStringAsync(), "client_secret"),
            ["invalid_redirect"] = RedirectUriValue(await redirectRequest.Content!.ReadAsStringAsync()),
            ["unsupported_scope"] = JsonPropertyValue(await scopeRequest.Content!.ReadAsStringAsync(), "scope"),
            ["authorization"] = HeaderValue(credentialRequest, "Authorization", "Bearer "),
            ["query"] = QueryValue(credentialRequest.RequestUri!.OriginalString, "client_id"),
            ["cookie"] = HeaderValue(credentialRequest, "Cookie", "session="),
            ["exception"] = exceptionFactory().Message,
            ["certificate_path"] = ArgumentValue(privateKeyJwtArguments, "--Bridge:Upstream:ClientAuthentication:CertificatePath"),
            ["configured_header"] = ArgumentValue(arguments, "--Bridge:Upstream:McpHeaders:0:Values:0"),
            ["response"] = responseBody,
            ["custom_header"] = HeaderValue(credentialRequest, "X-Custom-Canary", string.Empty),
            ["authorization_challenge"] = QueryValue(authorizeValidRequest.RequestUri!.OriginalString, "code_challenge"),
            ["authorization_state"] = QueryValue(authorizeValidRequest.RequestUri!.OriginalString, "state"),
            ["authorization_scope"] = QueryValue(authorizeScopeRejectedRequest.RequestUri!.OriginalString, "scope"),
            ["token_code"] = FormValue(await tokenAuthorizationCodeRequest.Content!.ReadAsStringAsync(), "code"),
            ["token_verifier"] = FormValue(await tokenAuthorizationCodeRequest.Content!.ReadAsStringAsync(), "code_verifier"),
            ["token_refresh_token"] = FormValue(await tokenRefreshRequest.Content!.ReadAsStringAsync(), "refresh_token"),
        };
    }

    private static void AssertInputSurfaces(TestCanaries canaries, Dictionary<string, string> surfaces)
    {
        Assert.Equal(TestCanaries.InputSurfaceNames, surfaces.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(canaries.All.Order(StringComparer.Ordinal), surfaces.Values.Order(StringComparer.Ordinal));
    }

    private static string ArgumentValue(IReadOnlyList<string> arguments, string name)
    {
        var index = Enumerable.Range(0, arguments.Count).SingleOrDefault(candidate => arguments[candidate] == name, -1);
        Assert.InRange(index, 0, arguments.Count - 2);
        return arguments[index + 1];
    }

    private static string JsonPropertyValue(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty(property, out var value));
        return value.GetString()!;
    }

    private static string RedirectUriValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        var redirectUri = document.RootElement.GetProperty("redirect_uris")[0].GetString()!;
        return new Uri(redirectUri, UriKind.Absolute).Segments[^1];
    }

    private static string QueryValue(string path, string key)
    {
        var query = path[(path.IndexOf('?') + 1)..];
        var value = query.Split('&', StringSplitOptions.None).Single(pair => pair.StartsWith($"{key}=", StringComparison.Ordinal));
        return value[(key.Length + 1)..];
    }

    private static string FormValue(string body, string key) => QueryValue($"?{body}", key);

    private static string HeaderValue(HttpRequestMessage request, string name, string prefix)
    {
        var value = request.Headers.GetValues(name).Single();
        Assert.StartsWith(prefix, value, StringComparison.Ordinal);
        return value[prefix.Length..];
    }

    private static IResult ThrowObservedException(Func<Exception> exceptionFactory, TaskCompletionSource<string> observedException)
    {
        var exception = exceptionFactory();
        observedException.TrySetResult(exception.Message);
        throw exception;
    }

    private static void AssertRegistrationError(CapturedResponse artifact, string error)
    {
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, artifact.StatusCode);
        Assert.Equal("application/json", artifact.ContentType);
        Assert.Equal($"{{\"error\":\"{error}\",\"error_description\":\"invalid client metadata\"}}", artifact.Body);
        Assert.DoesNotContain("WWW-Authenticate", artifact.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertRegistrationLog(CapturedLog entry)
    {
        Assert.Equal("registration", entry.State["Route"]);
        Assert.Equal("400", entry.State["StatusCode"]);
        Assert.Equal("4xx", entry.State["StatusClass"]);
        Assert.Equal("failure", entry.State["Result"]);
    }

    private static void AssertRegistrationActivity(CapturedActivity activity)
    {
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("registration", activity.Tags["bridge.route"]);
        Assert.Equal("failure", activity.Tags["bridge.result"]);
        Assert.Empty(activity.Events);
        Assert.Empty(activity.Baggage);
    }

    private static void AssertRegistrationMeasurement(CapturedMeasurement measurement)
    {
        Assert.True(measurement.Name is "bridge.requests" or "bridge.request.duration");
        Assert.Equal("registration", measurement.Tags["route"]);
        Assert.Equal("4xx", measurement.Tags["status"]);
        Assert.Equal(measurement.Name == "bridge.requests" ? "long" : "double", measurement.Kind);
    }

    private static void AssertCanariesAreAbsent(IEnumerable<string> canaries, string artifacts, params string[] exclusions)
    {
        foreach (var canary in canaries.Except(exclusions, StringComparer.Ordinal))
        {
            Assert.DoesNotContain(canary, artifacts, StringComparison.Ordinal);
        }
    }

    private static string FlattenArtifacts(IEnumerable<CapturedLog> logs, IEnumerable<CapturedActivity> activities, IEnumerable<CapturedMeasurement> measurements, IEnumerable<CapturedResponse>? responses = null) => string.Join('\n', logs.Select(entry => entry.ToString()).Concat(activities.Select(activity => activity.ToString())).Concat(measurements.Select(measurement => measurement.ToString())).Concat(responses?.Select(response => response.ToString()) ?? []));

    private static string FlattenArtifacts(IEnumerable<CapturedResponse> responses) => string.Join('\n', responses.Select(response => response.ToString()));

    [LoggerMessage(LogLevel.Error, "Rejected test category {Canary}")]
    private static partial void LogRejectedCategory(ILogger logger, string canary);

    private sealed class TelemetryCapture : IDisposable
    {
        public ConcurrentQueue<CapturedLog> Logs { get; } = new();
        public ConcurrentQueue<CapturedActivity> Activities { get; } = new();
        public ConcurrentQueue<CapturedMeasurement> Measurements { get; } = new();
        public CapturingLoggerProvider LoggerProvider { get; }
        private readonly ActivityListener activityListener;
        private readonly MeterListener meterListener;

        public TelemetryCapture()
        {
            LoggerProvider = new CapturingLoggerProvider(Logs);
            activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "McpOAuthDcrBridge",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Activities.Enqueue(new CapturedActivity(
                    activity.OperationName,
                    activity.Status,
                    activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? string.Empty, StringComparer.Ordinal),
                    activity.Events.SelectMany(activityEvent => activityEvent.Tags.Select(tag => new KeyValuePair<string, string>($"{activityEvent.Name}:{tag.Key}", tag.Value?.ToString() ?? string.Empty))).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                    activity.Baggage.ToDictionary(item => item.Key, item => item.Value ?? string.Empty, StringComparer.Ordinal))),
            };
            ActivitySource.AddActivityListener(activityListener);
            meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "McpOAuthDcrBridge") listener.EnableMeasurementEvents(instrument);
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Measurements.Enqueue(new CapturedMeasurement(instrument.Name, "long", ToTags(tags))));
            meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Measurements.Enqueue(new CapturedMeasurement(instrument.Name, "double", ToTags(tags))));
            meterListener.Start();
        }

        public void Dispose()
        {
            meterListener.Dispose();
            activityListener.Dispose();
            LoggerProvider.Dispose();
        }

        private static Dictionary<string, string> ToTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) => tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<CapturedLog> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => entries.Enqueue(new CapturedLog(category, logLevel, eventId, exception?.ToString(), formatter(state, exception), ToState(state)));
        private static Dictionary<string, string> ToState<TState>(TState state) => state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToDictionary(field => field.Key, field => field.Value?.ToString() ?? string.Empty, StringComparer.Ordinal) : [];
    }

    private sealed record CapturedResponse(System.Net.HttpStatusCode StatusCode, string? ContentType, Dictionary<string, string> Headers, string Body)
    {
        public override string ToString() => $"{StatusCode} {ContentType} {string.Join(';', Headers.Select(header => $"{header.Key}={header.Value}"))} {Body}";
    }

    private sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string? Exception, string Message, Dictionary<string, string> State)
    {
        public override string ToString() => $"{Category} {Level} {Exception} {Message} {string.Join(';', State.Select(field => $"{field.Key}={field.Value}"))}";
    }

    private sealed record CapturedActivity(string Name, ActivityStatusCode Status, Dictionary<string, string> Tags, Dictionary<string, string> Events, Dictionary<string, string> Baggage)
    {
        public override string ToString() => $"{Name} {Status} {string.Join(';', Tags.Select(tag => $"{tag.Key}={tag.Value}"))} {string.Join(';', Events.Select(item => $"{item.Key}={item.Value}"))} {string.Join(';', Baggage.Select(item => $"{item.Key}={item.Value}"))}";
    }

    private sealed record CapturedMeasurement(string Name, string Kind, Dictionary<string, string> Tags)
    {
        public override string ToString() => $"{Name} {Kind} {string.Join(';', Tags.Select(tag => $"{tag.Key}={tag.Value}"))}";
    }

    private sealed record TestCanaries(
        string ConfiguredSecret,
        string RegistrationSecret,
        string InvalidRedirect,
        string UnsupportedScope,
        string Authorization,
        string Query,
        string Cookie,
        string Exception,
        string CertificatePath,
        string ConfiguredHeader,
        string Response,
        string CustomHeader,
        string AuthorizationChallenge,
        string AuthorizationState,
        string AuthorizationScope,
        string TokenCode,
        string TokenVerifier,
        string TokenRefreshToken)
    {
        public static IReadOnlyList<string> InputSurfaceNames =>
        [
            "authorization", "authorization_challenge", "authorization_scope", "authorization_state",
            "certificate_path", "configured_header", "configured_secret",
            "cookie", "custom_header", "exception", "invalid_redirect", "query",
            "registration_secret", "response", "token_code", "token_refresh_token", "token_verifier", "unsupported_scope",
        ];

        public IReadOnlyList<string> All =>
        [
            ConfiguredSecret, RegistrationSecret, InvalidRedirect, UnsupportedScope,
            Authorization, Query, Cookie, Exception, CertificatePath,
            ConfiguredHeader, Response, CustomHeader,
            AuthorizationChallenge, AuthorizationState, AuthorizationScope,
            TokenCode, TokenVerifier, TokenRefreshToken,
        ];
    }
}
