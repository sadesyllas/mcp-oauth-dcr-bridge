using System.Text.Json;
using Xunit;

namespace McpOAuthDcrBridge.UnitTests.Documentation;

/// <summary>
/// Validates that <c>docs/openapi.json</c> is well-formed and stays consistent with every
/// bridge-owned endpoint mapped by <see cref="McpOAuthDcrBridge.BridgeApplication.Build(string[])"/>,
/// so the machine-readable documentation cannot silently drift from the implementation.
/// </summary>
public sealed class OpenApiDocumentTests
{
    private static readonly string[] ExpectedPaths =
    [
        "/.well-known/oauth-authorization-server",
        "/.well-known/oauth-protected-resource",
        "/register",
        "/authorize",
        "/token",
        "/health/live",
        "/health/ready",
        "/mcp",
    ];

    private static readonly string DocumentPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "openapi.json");

    [Fact]
    public void DocumentIsWellFormedJson()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DocumentPath));

        Assert.Equal("3.0.3", document.RootElement.GetProperty("openapi").GetString());
        Assert.True(document.RootElement.TryGetProperty("info", out _));
        Assert.True(document.RootElement.TryGetProperty("paths", out _));
    }

    [Fact]
    public void EveryBridgeOwnedRouteIsDocumented()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DocumentPath));
        var paths = document.RootElement.GetProperty("paths");

        Assert.All(ExpectedPaths, path => Assert.True(paths.TryGetProperty(path, out _), $"Missing OpenAPI path: {path}"));
        Assert.Equal(ExpectedPaths.Length, paths.EnumerateObject().Count());
    }

    [Fact]
    public void EveryOperationHasAnOperationIdAndAtLeastOneResponse()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DocumentPath));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                Assert.True(operation.Value.TryGetProperty("operationId", out _), $"{path.Name} {operation.Name} is missing operationId");
                Assert.True(operation.Value.GetProperty("responses").EnumerateObject().Any(), $"{path.Name} {operation.Name} declares no responses");
            }
        }
    }

    [Fact]
    public void NoExampleContainsAnActualSecretOrKeyMaterialShape()
    {
        var text = File.ReadAllText(DocumentPath);

        Assert.DoesNotContain("BEGIN CERTIFICATE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"client_secret\":", text, StringComparison.Ordinal);
    }
}
