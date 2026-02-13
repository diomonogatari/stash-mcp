using Bitbucket.Net.Models.Core.Projects;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace StashMcpServer.IntegrationTests;

public sealed class ProjectToolsIntegrationTests
{
    [Fact]
    public async Task ListProjects_WithCachedProjects_ReturnsSortedFormattedOutput()
    {
        await using var factory = new StashMcpTestFactory(f =>
        {
            f.CacheService.GetProjects().Returns(
                [
                    new Project { Key = "BETA", Name = "Beta" },
                    new Project { Key = "ALPHA", Name = "Alpha" },
                ]);
        });

        await using var client = await factory.CreateClientAsync();

        var result = await client.CallToolAsync("list_projects", arguments: new Dictionary<string, object?>());
        var text = result.Content.OfType<TextContentBlock>().First().Text;

        Assert.Contains("Projects (2)", text);
        Assert.Contains("- Alpha [ALPHA]", text);
        Assert.Contains("- Beta [BETA]", text);

        var alphaIndex = text.IndexOf("- Alpha [ALPHA]", StringComparison.Ordinal);
        var betaIndex = text.IndexOf("- Beta [BETA]", StringComparison.Ordinal);
        Assert.True(alphaIndex < betaIndex, "Expected projects to be sorted alphabetically by name.");
    }

    [Fact]
    public async Task ListProjects_WhenCacheIsEmpty_ReturnsEmptyMessage()
    {
        await using var factory = new StashMcpTestFactory(f =>
        {
            f.CacheService.GetProjects().Returns(Array.Empty<Project>());
        });

        await using var client = await factory.CreateClientAsync();

        var result = await client.CallToolAsync("list_projects", arguments: new Dictionary<string, object?>());
        var text = result.Content.OfType<TextContentBlock>().First().Text;

        Assert.Equal("No projects found.", text);
    }
}