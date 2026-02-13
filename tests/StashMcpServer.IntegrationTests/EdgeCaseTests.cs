using Bitbucket.Net.Models.Core.Projects;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace StashMcpServer.IntegrationTests;

public sealed class EdgeCaseTests
{
    [Fact]
    public async Task CallTool_ToolNotFound_ReturnsErrorOrThrowsNotFound()
    {
        await using var factory = new StashMcpTestFactory();
        await using var client = await factory.CreateClientAsync();

        Exception? capturedException = null;
        string? resultText = null;

        try
        {
            var result = await client.CallToolAsync(
                "tool_that_does_not_exist",
                arguments: new Dictionary<string, object?>());

            resultText = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            Assert.True(result.IsError, "Expected unknown tool call to return an MCP error result.");
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }

        if (capturedException is not null)
        {
            Assert.Contains("unknown tool", capturedException.Message, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.NotNull(resultText);
        Assert.Contains("unknown tool", resultText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallTool_DependencyThrows_ReturnsErrorOrThrows()
    {
        await using var factory = new StashMcpTestFactory(f =>
        {
            f.CacheService
                .When(cache => cache.GetProjects())
                .Do(_ => throw new InvalidOperationException("boom from cache"));
        });

        await using var client = await factory.CreateClientAsync();

        Exception? capturedException = null;
        string? resultText = null;

        try
        {
            var result = await client.CallToolAsync("list_projects", arguments: new Dictionary<string, object?>());
            resultText = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

            Assert.True(result.IsError, "Expected tool failure to be surfaced as MCP error result.");
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }

        if (capturedException is not null)
        {
            Assert.Contains("list_projects", capturedException.Message, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.NotNull(resultText);
        Assert.Contains("error occurred invoking", resultText!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list_projects", resultText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParallelFactories_AreIsolated()
    {
        await using var factoryA = new StashMcpTestFactory(f =>
        {
            f.CacheService.GetProjects().Returns([new Project { Key = "A", Name = "Alpha" }]);
        });

        await using var factoryB = new StashMcpTestFactory(f =>
        {
            f.CacheService.GetProjects().Returns([new Project { Key = "B", Name = "Beta" }]);
        });

        await using var clientA = await factoryA.CreateClientAsync();
        await using var clientB = await factoryB.CreateClientAsync();

        var callA = clientA.CallToolAsync("list_projects", arguments: new Dictionary<string, object?>()).AsTask();
        var callB = clientB.CallToolAsync("list_projects", arguments: new Dictionary<string, object?>()).AsTask();

        await Task.WhenAll(callA, callB);

        var textA = (await callA).Content.OfType<TextContentBlock>().First().Text;
        var textB = (await callB).Content.OfType<TextContentBlock>().First().Text;

        Assert.Contains("Alpha [A]", textA);
        Assert.DoesNotContain("Beta [B]", textA);

        Assert.Contains("Beta [B]", textB);
        Assert.DoesNotContain("Alpha [A]", textB);
    }

    [Fact]
    public async Task DisposeAsync_DuringInFlightCall_DoesNotHang()
    {
        var factory = new StashMcpTestFactory();
        var client = await factory.CreateClientAsync();

        var inFlight = client.ListToolsAsync().AsTask();
        var dispose = factory.DisposeAsync().AsTask();
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));

        var completion = await Task.WhenAny(Task.WhenAll(inFlight, dispose), timeout);

        Assert.NotEqual(timeout, completion);

        // Factory disposal owns client disposal. Ensure we don't leak references in test scope.
        GC.KeepAlive(client);
    }
}
