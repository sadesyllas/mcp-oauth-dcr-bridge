using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using McpOAuthDcrBridge.Configuration;
using McpOAuthDcrBridge.OAuth;
using McpOAuthDcrBridge.Telemetry;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace McpOAuthDcrBridge.Token;

/// <summary>
/// Maps the stateless token-forwarding endpoint that exchanges authorization codes and refresh tokens
/// with the fixed upstream token endpoint under the configured upstream client authentication.
/// </summary>
public static class TokenEndpointExtensions
{
    /// <summary>The named <see cref="IHttpClientFactory"/> client used for outbound token requests.</summary>
    public const string HttpClientName = "UpstreamOAuth";

    private static readonly ImmutableHashSet<string> SingleValuedParameters = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "grant_type", "client_id", "code", "code_verifier", "redirect_uri", "refresh_token", "scope");
    private static readonly ImmutableHashSet<string> RejectedFields = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "client_secret", "client_assertion", "client_assertion_type");

    /// <summary>Maps the bridge's token-forwarding endpoint.</summary>
    /// <param name="application">The application endpoint route builder.</param>
    /// <param name="options">The validated bridge configuration.</param>
    /// <returns>The same application for composition.</returns>
    public static WebApplication MapTokenEndpoint(this WebApplication application, BridgeOptions options)
    {
        application.MapPost("/token", (HttpContext context, IHttpClientFactory httpClientFactory) => TokenAsync(context, options, httpClientFactory)).RequireRateLimiting("token");
        return application;
    }

    private static async Task<IResult> TokenAsync(HttpContext context, BridgeOptions options, IHttpClientFactory httpClientFactory)
    {
        var request = context.Request;

        // A downstream Authorization header is never legitimate on a public-client token request and
        // is rejected outright rather than silently ignored, to close the credential-smuggling surface.
        if (request.Headers.ContainsKey("Authorization")) return Error("invalid_request");
        if (!IsFormContentType(request.ContentType)) return Error("invalid_request");

        var bytes = await BoundedRequestBody.ReadAsync(request, options.Limits.TokenRequestBodyBytes, context.RequestAborted);
        if (bytes is null) return Error("invalid_request");

        var form = QueryHelpers.ParseQuery(Encoding.UTF8.GetString(bytes));
        if (OAuthFormParameters.HasDuplicate(form, SingleValuedParameters) || RejectedFields.Any(form.ContainsKey)) return Error("invalid_request");
        if (!OAuthFormParameters.TrySingleValue(form, "client_id", out var clientId) || clientId != options.ClientId) return Error("invalid_client");
        if (!OAuthFormParameters.TrySingleValue(form, "grant_type", out var grantType)) return Error("invalid_request");

        var validationError = grantType switch
        {
            "authorization_code" => ValidateAuthorizationCode(form, options),
            "refresh_token" => ValidateRefreshToken(form),
            _ => "unsupported_grant_type",
        };
        if (validationError is not null) return Error(validationError);

        return await ForwardAsync(context, options, httpClientFactory, form, grantType);
    }

    private static string? ValidateAuthorizationCode(Dictionary<string, StringValues> form, BridgeOptions options)
    {
        if (!OAuthFormParameters.TrySingleValue(form, "redirect_uri", out var redirectUri) || !options.AllowedRedirectUris.Contains(redirectUri)) return "invalid_grant";
        if (!OAuthFormParameters.TrySingleValue(form, "code", out var code) || code.Length == 0) return "invalid_request";
        if (!OAuthFormParameters.TrySingleValue(form, "code_verifier", out var verifier) || verifier.Length == 0) return "invalid_request";
        return null;
    }

    private static string? ValidateRefreshToken(Dictionary<string, StringValues> form) =>
        OAuthFormParameters.TrySingleValue(form, "refresh_token", out var refreshToken) && refreshToken.Length > 0 ? null : "invalid_request";

    private static async Task<IResult> ForwardAsync(HttpContext context, BridgeOptions options, IHttpClientFactory httpClientFactory, Dictionary<string, StringValues> form, string grantType)
    {
        using var forwardRequest = new HttpRequestMessage(HttpMethod.Post, options.UpstreamTokenEndpoint);
        var fields = form.SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string>(pair.Key, value ?? string.Empty))).ToList();
        await UpstreamClientAuthenticator.ApplyAsync(forwardRequest, fields, options, context.RequestAborted);
        forwardRequest.Content = new FormUrlEncodedContent(fields);

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var stopwatch = Stopwatch.StartNew();
        using var activity = BridgeTelemetry.ActivitySource.StartActivity("bridge.upstream.oauth");
        activity?.SetTag("bridge.grant", grantType);
        try
        {
            var upstreamResponse = await httpClient.SendAsync(forwardRequest, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            RecordUpstreamOAuth(activity, stopwatch, grantType, TelemetryRedactor.ResultCategory((int)upstreamResponse.StatusCode));
            return new UpstreamTokenResponseResult(upstreamResponse);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            RecordUpstreamOAuth(activity, stopwatch, grantType, "cancelled");
            throw;
        }
        catch (OperationCanceledException)
        {
            RecordUpstreamOAuth(activity, stopwatch, grantType, "timeout");
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            RecordUpstreamOAuth(activity, stopwatch, grantType, "error");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static void RecordUpstreamOAuth(Activity? activity, Stopwatch stopwatch, string grantType, string status)
    {
        stopwatch.Stop();
        activity?.SetTag("bridge.result", status);
        if (status is "error" or "timeout" or "cancelled") activity?.SetStatus(ActivityStatusCode.Error);
        var tags = new KeyValuePair<string, object?>[] { new("grant", grantType), new("status", status) };
        BridgeTelemetry.UpstreamOAuthRequestCount.Add(1, tags);
        BridgeTelemetry.UpstreamOAuthDurationMilliseconds.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }

    private static bool IsFormContentType(string? contentType) =>
        contentType is not null && MediaTypeHeaderValue.TryParse(contentType, out var mediaType) && mediaType.MediaType == "application/x-www-form-urlencoded";

    private static IResult Error(string code) => OAuthErrorResult.Json("token", code, "invalid token request");
}
