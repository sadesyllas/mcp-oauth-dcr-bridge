var builder = WebApplication.CreateBuilder(args);
var application = builder.Build();

await application.RunAsync();

/// <summary>
/// Provides a public entry-point marker for in-process host testing.
/// </summary>
public partial class Program;
