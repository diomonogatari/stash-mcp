namespace StashMcpServer.Services;

/// <summary>
/// Settings for the MCP server operational mode.
/// </summary>
public interface IServerSettings
{
    bool ReadOnlyMode { get; }

    /// <summary>
    /// Optional list of project keys to scope startup caching to.
    /// When empty, the server derives target projects from the user's recent repositories.
    /// </summary>
    IReadOnlyList<string> Projects { get; }
}