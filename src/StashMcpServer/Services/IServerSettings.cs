namespace StashMcpServer.Services;

/// <summary>
/// Settings for the MCP server operational mode.
/// </summary>
public interface IServerSettings
{
    bool ReadOnlyMode { get; }
}