namespace McpOAuthDcrBridge.Security;

/// <summary>
/// Adds the bridge's bounded security response headers to an application host.
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>Adds the security headers middleware to the pipeline.</summary>
    /// <param name="application">The application pipeline.</param>
    /// <returns>The same application for composition.</returns>
    public static WebApplication UseSecurityHeaders(this WebApplication application)
    {
        application.UseMiddleware<SecurityHeadersMiddleware>();
        return application;
    }
}
