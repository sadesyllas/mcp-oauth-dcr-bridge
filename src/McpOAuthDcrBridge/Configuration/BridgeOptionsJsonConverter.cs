using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpOAuthDcrBridge.Configuration;

/// <summary>
/// Writes a diagnostic-safe representation of bridge options without credential or header values.
/// </summary>
public sealed class BridgeOptionsJsonConverter : JsonConverter<BridgeOptions>
{
    /// <inheritdoc />
    public override BridgeOptions Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Bridge options are created only through validated configuration.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BridgeOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(BridgeOptions.ExternalBaseUri), value.ExternalBaseUri.AbsoluteUri);
        writer.WriteString(nameof(BridgeOptions.UpstreamAuthorizationEndpoint), value.UpstreamAuthorizationEndpoint.AbsoluteUri);
        writer.WriteString(nameof(BridgeOptions.UpstreamTokenEndpoint), value.UpstreamTokenEndpoint.AbsoluteUri);
        writer.WriteString(nameof(BridgeOptions.UpstreamMcpUri), value.UpstreamMcpUri.AbsoluteUri);
        writer.WriteString(nameof(BridgeOptions.ClientId), value.ClientId);
        writer.WritePropertyName(nameof(BridgeOptions.AllowedRedirectUris));
        JsonSerializer.Serialize(writer, value.AllowedRedirectUris, options);
        writer.WritePropertyName(nameof(BridgeOptions.AllowedScopes));
        JsonSerializer.Serialize(writer, value.AllowedScopes, options);
        writer.WritePropertyName(nameof(BridgeOptions.Limits));
        JsonSerializer.Serialize(writer, value.Limits, options);
        writer.WriteString(nameof(BridgeOptions.OtlpEndpoint), value.OtlpEndpoint?.AbsoluteUri);
        writer.WriteEndObject();
    }
}
