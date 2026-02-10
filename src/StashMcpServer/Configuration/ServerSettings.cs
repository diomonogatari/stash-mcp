using StashMcpServer.Services;
using System.Collections.Immutable;

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
    /// Optional list of project keys to scope startup caching to.
    /// When empty, the server derives target projects from the user's recent repositories.
    /// </summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>
    /// Creates settings from environment variables.
    /// </summary>
    public static ServerSettings FromEnvironment()
    {
        var readOnlyEnv = Environment.GetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE");
        var readOnlyMode = string.Equals(readOnlyEnv, "true", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(readOnlyEnv, "1", StringComparison.Ordinal);

        var projects = ParseProjectKeys(Environment.GetEnvironmentVariable("BITBUCKET_PROJECTS"));

        return new ServerSettings { ReadOnlyMode = readOnlyMode, Projects = projects };
    }

    private static ImmutableList<string> ParseProjectKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableList();
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