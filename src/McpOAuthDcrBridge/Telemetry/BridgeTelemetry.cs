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

    /// <summary>Gets the counter for outbound OAuth requests.</summary>
    public static Counter<long> UpstreamOAuthRequestCount { get; } = Meter.CreateCounter<long>("bridge.upstream.oauth.requests");

    /// <summary>Gets the duration histogram for outbound OAuth requests.</summary>
    public static Histogram<double> UpstreamOAuthDurationMilliseconds { get; } = Meter.CreateHistogram<double>("bridge.upstream.oauth.duration", "ms");

    /// <summary>Gets the counter for outbound MCP requests.</summary>
    public static Counter<long> UpstreamMcpRequestCount { get; } = Meter.CreateCounter<long>("bridge.upstream.mcp.requests");

    /// <summary>Gets the duration histogram for outbound MCP requests.</summary>
    public static Histogram<double> UpstreamMcpDurationMilliseconds { get; } = Meter.CreateHistogram<double>("bridge.upstream.mcp.duration", "ms");

    /// <summary>Gets the counter for bounded validation rejections.</summary>
    public static Counter<long> ValidationRejectionCount { get; } = Meter.CreateCounter<long>("bridge.validation.rejections");

    /// <summary>Gets the counter for bounded proxy transport failures.</summary>
    public static Counter<long> ProxyFailureCount { get; } = Meter.CreateCounter<long>("bridge.proxy.failures");

    /// <summary>Gets the active MCP request and stream count.</summary>
    public static UpDownCounter<long> ActiveMcpRequests { get; } = Meter.CreateUpDownCounter<long>("bridge.mcp.active");
}
