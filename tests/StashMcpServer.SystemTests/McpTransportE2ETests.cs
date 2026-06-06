using ModelContextProtocol.Protocol;
using StashMcpServer.SystemTests.Infrastructure;

namespace StashMcpServer.SystemTests;

/// <summary>
/// End-to-end tests that drive the tools the way a model does: through a real MCP client over the
/// in-memory transport, against a live, seeded Bitbucket container. This covers the protocol layer
/// (tools/call serialization, content blocks) on top of the real service pipeline.
/// Skipped unless <c>STASH_TEST_LICENSE</c> + Docker are present.
/// </summary>
[Collection(SystemTestCollection.Name)]
[Trait("Category", "SystemTest")]
public sealed class McpTransportE2ETests(BitbucketServerFixture fixture)
{
    [SkippableFact]
    public async Task ListProjects_OverMcpTransport_ReturnsSeededProject()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason);

        await using var factory = fixture.CreateFactory();
        await using var client = await factory.CreateClientAsync();
        await factory.WarmCacheAsync(); // list_projects is cache-backed; populate it deterministically.

        var result = await client.CallToolAsync("list_projects", arguments: new Dictionary<string, object?>());
        var text = result.Content.OfType<TextContentBlock>().First().Text;

        Assert.Contains(fixture.Seeded.ProjectKey, text);
    }

    [SkippableFact]
    public async Task GetRepositoryOverview_OverMcpTransport_ReturnsSeededTag()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason);

        await using var factory = fixture.CreateFactory();
        await using var client = await factory.CreateClientAsync();

        // get_repository_overview reads through to the live server, so no cache warmup is required.
        var result = await client.CallToolAsync("get_repository_overview", arguments: new Dictionary<string, object?>
        {
            ["projectKey"] = fixture.Seeded.ProjectKey,
            ["repositorySlug"] = fixture.Seeded.RepositorySlug,
        });
        var text = result.Content.OfType<TextContentBlock>().First().Text;

        Assert.Contains(fixture.Seeded.TagName, text);
    }
}