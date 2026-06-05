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

    /// <summary>
    /// The 9 write tools — the exact set guarded by <c>CheckReadOnlyMode()</c>; every other tool is read-only.
    /// This is the single source of truth for the annotation drift guard: adding or removing a write tool
    /// without updating this set (or the tool's <c>ReadOnly</c> annotation) fails
    /// <see cref="ListTools_ReadOnlyHints_MatchWriteClassification"/>.
    /// </summary>
    private static readonly HashSet<string> WriteToolNames = new(StringComparer.Ordinal)
    {
        "reply_to_pull_request_comment",
        "add_pull_request_comment",
        "create_pull_request",
        "update_pull_request",
        "merge_pull_request",
        "approve_pull_request",
        "create_pull_request_task",
        "update_pull_request_task",
        "delete_pull_request_task",
    };

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

    [Fact]
    public async Task ListTools_ReadOnlyHints_MatchWriteClassification()
    {
        var tools = await ListToolsAsync();

        foreach (var tool in tools)
        {
            var readOnlyHint = tool.ProtocolTool.Annotations?.ReadOnlyHint;
            if (WriteToolNames.Contains(tool.Name))
            {
                Assert.False(readOnlyHint == true, $"Write tool '{tool.Name}' must not advertise readOnlyHint=true.");
            }
            else
            {
                Assert.True(readOnlyHint == true, $"Read tool '{tool.Name}' must advertise readOnlyHint=true.");
            }
        }
    }

    [Fact]
    public async Task ListTools_AllToolsHaveTitle()
    {
        var tools = await ListToolsAsync();

        Assert.All(tools, tool => Assert.False(
            string.IsNullOrWhiteSpace(tool.ProtocolTool.Annotations?.Title),
            $"Tool '{tool.Name}' is missing an annotation title."));
    }

    [Fact]
    public async Task ListTools_DestructiveAndIdempotentHints_MatchPolicy()
    {
        var tools = (await ListToolsAsync()).ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        Assert.True(tools["merge_pull_request"].ProtocolTool.Annotations?.DestructiveHint == true, "merge_pull_request must advertise destructiveHint=true.");
        Assert.True(tools["delete_pull_request_task"].ProtocolTool.Annotations?.DestructiveHint == true, "delete_pull_request_task must advertise destructiveHint=true.");
        Assert.True(tools["approve_pull_request"].ProtocolTool.Annotations?.IdempotentHint == true, "approve_pull_request must advertise idempotentHint=true.");
        Assert.True(tools["delete_pull_request_task"].ProtocolTool.Annotations?.IdempotentHint == true, "delete_pull_request_task must advertise idempotentHint=true.");
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