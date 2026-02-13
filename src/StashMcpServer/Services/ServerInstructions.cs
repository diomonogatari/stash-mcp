using System.Reflection;

namespace StashMcpServer.Services;

/// <summary>
/// Provides server instructions for LLM consumers of the MCP server.
/// These instructions guide optimal tool selection for common workflows.
/// Loaded once from an embedded resource at startup.
/// </summary>
public static class ServerInstructions
{
    private const string ResourceName = "StashMcpServer.ServerInstructions.txt";

    private static readonly string Instructions = LoadInstructions();

    /// <summary>
    /// Returns the server instructions loaded from the embedded resource.
    /// </summary>
    public static string Generate() => Instructions;

    private static string LoadInstructions()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}