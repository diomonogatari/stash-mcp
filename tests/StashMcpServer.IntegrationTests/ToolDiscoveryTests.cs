using ModelContextProtocol.Client;
using System.Text.Json;

namespace StashMcpServer.IntegrationTests;

public sealed class ToolDiscoveryTests
{
    /// <summary>
    /// Canonical number of MCP tools exposed by the server. Keep this in sync with the
    /// documented count in README.md, the Dockerfile label, registry/docker-mcp metadata,
    /// and docs/TOOLSET.md. This exact-count guard exists to catch documentation/source drift.
    /// </summary>
    private const int ExpectedToolCount = 41;

    [Fact]
    public async Task ListTools_ReturnsExactCanonicalCount()
    {
        var tools = await ListToolsAsync();

        Assert.Equal(ExpectedToolCount, tools.Count);
    }

    [Fact]
    public async Task ListTools_ContainsProjectTools()
    {
        var toolNames = await ListToolNamesAsync();

        Assert.Contains("list_projects", toolNames);
        Assert.Contains("list_repositories", toolNames);
        Assert.Contains("get_repository_overview", toolNames);
    }

    [Fact]
    public async Task ListTools_ContainsPullRequestTools()
    {
        var toolNames = await ListToolNamesAsync();

        Assert.Contains("list_pull_requests", toolNames);
        Assert.Contains("get_pull_request", toolNames);
        Assert.Contains("get_pull_request_diff", toolNames);
        Assert.Contains("merge_pull_request", toolNames);
        Assert.Contains("approve_pull_request", toolNames);
        Assert.Contains("create_pull_request_task", toolNames);
    }

    [Fact]
    public async Task ListTools_ContainsSearchTools()
    {
        var toolNames = await ListToolNamesAsync();

        Assert.Contains("search_code", toolNames);
        Assert.Contains("search_commits", toolNames);
        Assert.Contains("search_pull_requests", toolNames);
        Assert.Contains("search_users", toolNames);
    }

    [Fact]
    public async Task ListTools_ContainsDashboardTools()
    {
        var toolNames = await ListToolNamesAsync();

        Assert.Contains("get_my_pull_requests", toolNames);
        Assert.Contains("get_inbox_pull_requests", toolNames);
        Assert.Contains("get_recent_repositories", toolNames);
        Assert.Contains("get_current_user", toolNames);
    }

    [Fact]
    public async Task ListTools_AllToolsHaveDescriptions()
    {
        var tools = await ListToolsAsync();

        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"Tool '{tool.Name}' is missing a description."));
    }

    [Fact]
    public async Task ListTools_AllToolsHaveInputSchemas()
    {
        var tools = await ListToolsAsync();

        Assert.All(tools, tool => Assert.NotEqual(JsonValueKind.Undefined, tool.JsonSchema.ValueKind));
    }

    private static async Task<IReadOnlyList<string>> ListToolNamesAsync()
    {
        var tools = await ListToolsAsync();
        return tools.Select(tool => tool.Name).ToArray();
    }

    private static async Task<IReadOnlyList<McpClientTool>> ListToolsAsync()
    {
        await using var factory = new StashMcpTestFactory();
        await using var client = await factory.CreateClientAsync();

        var tools = await client.ListToolsAsync();
        return tools.ToArray();
    }
}