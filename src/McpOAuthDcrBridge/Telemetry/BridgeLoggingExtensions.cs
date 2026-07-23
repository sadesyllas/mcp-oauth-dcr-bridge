using Microsoft.Extensions.Logging.Console;

namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Configures the bridge's safe console logging format for the active environment.
/// </summary>
public static class BridgeLoggingExtensions
{
    /// <summary>Uses JSON logs outside development and concise logs while developing locally.</summary>
    /// <param name="logging">The host logging builder.</param>
    /// <param name="isDevelopment">Whether the host is running in development.</param>
    public static void ConfigureBridgeLogging(this ILoggingBuilder logging, bool isDevelopment)
    {
        logging.ClearProviders();
        logging.AddFilter(SafeTelemetryPolicy.IsEnabled);
        if (isDevelopment)
        {
            logging.AddSimpleConsole(options => options.SingleLine = true);
        }
        else
        {
            logging.AddJsonConsole(options => options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false });
        }
    }
}
