using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace McpOAuthDcrBridge.Telemetry;

/// <summary>
/// Provides bridge-owned activity and metric instruments with bounded, protocol-neutral dimensions.
/// </summary>
public static class BridgeTelemetry
{
    /// <summary>Gets the activity source for bridge request spans.</summary>
    public static ActivitySource ActivitySource { get; } = new("McpOAuthDcrBridge");

    /// <summary>Gets the meter that exposes bridge request metrics.</summary>
    public static Meter Meter { get; } = new("McpOAuthDcrBridge");

    /// <summary>Gets the counter for inbound bridge requests.</summary>
    public static Counter<long> RequestCount { get; } = Meter.CreateCounter<long>("bridge.requests");

    /// <summary>Gets the histogram for inbound bridge request duration.</summary>
    public static Histogram<double> RequestDurationMilliseconds { get; } = Meter.CreateHistogram<double>("bridge.request.duration", "ms");
}
