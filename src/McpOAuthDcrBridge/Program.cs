using McpOAuthDcrBridge;

var application = BridgeApplication.Build(args);

await application.RunAsync();

/// <summary>
/// Provides a public entry-point marker for in-process host testing.
/// </summary>
public partial class Program;
