using StashMcpServer.Services;

namespace StashMcpServer.Configuration;

/// <summary>
/// Settings for the MCP server operational mode.
/// </summary>
public class ServerSettings : IServerSettings
{
    /// <summary>
    /// When true, all write operations are disabled and return an error message.
    /// This is useful for demos, exploration, or read-only access scenarios.
    /// </summary>
    public bool ReadOnlyMode { get; init; }

    /// <summary>
    /// Creates settings from environment variables.
    /// </summary>
    public static ServerSettings FromEnvironment()
    {
        var readOnlyEnv = Environment.GetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE");
        var readOnlyMode = string.Equals(readOnlyEnv, "true", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(readOnlyEnv, "1", StringComparison.Ordinal);

        return new ServerSettings { ReadOnlyMode = readOnlyMode };
    }

    /// <summary>
    /// Error message returned when a write operation is attempted in read-only mode.
    /// </summary>
    public const string ReadOnlyErrorMessage = """
        ❌ **Server is in Read-Only Mode**

        Write operations are disabled on this MCP server instance.
        
        This setting is controlled by the `BITBUCKET_READ_ONLY_MODE` environment variable.
        Contact your administrator to enable write operations.
        """;
}