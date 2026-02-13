using ModelContextProtocol.Protocol;
using NSubstitute;

namespace StashMcpServer.IntegrationTests;

public sealed class PullRequestToolsIntegrationTests
{
    [Fact]
    public async Task CreatePullRequest_ReadOnlyMode_ReturnsReadOnlyError()
    {
        await using var factory = new StashMcpTestFactory(f =>
        {
            f.ServerSettings.ReadOnlyMode.Returns(true);
        });

        await using var client = await factory.CreateClientAsync();

        var result = await client.CallToolAsync(
            "create_pull_request",
            arguments: new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["repositorySlug"] = "repo",
                ["title"] = "Test PR",
                ["sourceRef"] = "feature/test",
            });

        var text = result.Content.OfType<TextContentBlock>().First().Text;

        Assert.Contains("Read-Only Mode", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write operations are disabled", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddPullRequestComment_ReadOnlyMode_ReturnsReadOnlyError()
    {
        await using var factory = new StashMcpTestFactory(f =>
        {
            f.ServerSettings.ReadOnlyMode.Returns(true);
        });

        await using var client = await factory.CreateClientAsync();

        var result = await client.CallToolAsync(
            "add_pull_request_comment",
            arguments: new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["repositorySlug"] = "repo",
                ["pullRequestId"] = 123,
                ["text"] = "Looks great!",
            });

        var text = result.Content.OfType<TextContentBlock>().First().Text;

        Assert.Contains("Read-Only Mode", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write operations are disabled", text, StringComparison.OrdinalIgnoreCase);
    }
}