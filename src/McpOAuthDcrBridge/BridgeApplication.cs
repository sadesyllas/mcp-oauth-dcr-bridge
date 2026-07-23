namespace McpOAuthDcrBridge;

/// <summary>
/// Creates the bridge application host from its command-line configuration.
/// </summary>
public static class BridgeApplication
{
    /// <summary>
    /// Builds the endpoint-free application host used by the executable and lifecycle tests.
    /// </summary>
    /// <param name="args">The command-line configuration arguments for the host.</param>
    /// <returns>A configured application host that has not yet been started.</returns>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        return builder.Build();
    }
}
