using Bitbucket.Net;
using Bitbucket.Net.Common.Exceptions;
using Bitbucket.Net.Models.Core.Projects;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StashMcpServer.Configuration;
using StashMcpServer.Formatting;
using StashMcpServer.Services;
using StashMcpServer.Tools;

namespace StashMcpServer.IntegrationTests;

/// <summary>
/// End-to-end tests over the REAL service composition — the resilience pipeline, the in-memory
/// cache, and the formatters — with only <see cref="IBitbucketClient"/> (the HTTP boundary) mocked.
/// Tools are resolved from a real DI container and invoked directly, so these exercise the actual
/// hardened code paths (unified error mapping and the canonical timestamp format) rather than the
/// substituted services that <see cref="StashMcpTestFactory"/> uses. The MCP transport itself is
/// covered separately by <see cref="ToolDiscoveryTests"/> and the read-only-mode tests.
///
/// Note: <c>ResilientApiService</c> is not a read-through cache — it always calls the API and keeps
/// the result only as a graceful-degradation fallback — so caching/fallback behavior is covered by
/// the focused unit tests in <c>ResilientApiServiceTests</c>, not here.
/// </summary>
public sealed class RealCompositionIntegrationTests
{
    private static ServiceProvider BuildRealPipeline(IBitbucketClient client)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var resilience = new ResilienceSettings
        {
            MaxRetryAttempts = 1, // minimal retries in tests
            RequestTimeout = TimeSpan.FromSeconds(5),
            DynamicCacheTtl = TimeSpan.FromMinutes(5),
        };
        services.AddSingleton(resilience);
        services.AddSingleton<IServerSettings>(new ServerSettings());
        services.AddMemoryCache(options => options.SizeLimit = resilience.CacheSizeLimit);

        // Mock ONLY the HTTP boundary; everything below is the real production pipeline.
        services.AddSingleton(client);
        services.AddSingleton<IDiffFormatter, DiffFormatter>();
        services.AddSingleton<BitbucketCacheService>();
        services.AddSingleton<IBitbucketCacheService>(sp => sp.GetRequiredService<BitbucketCacheService>());
        services.AddSingleton<ResilientApiService>();
        services.AddSingleton<IResilientApiService>(sp => sp.GetRequiredService<ResilientApiService>());
        services.AddSingleton<PullRequestTools>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetPullRequest_NotFound_MapsToResourceNotFoundThroughRealPipeline()
    {
        var client = Substitute.For<IBitbucketClient>();
        client.GetPullRequestAsync("PROJ", "repo", 1, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BitbucketNotFoundException("missing", []));

        using var sp = BuildRealPipeline(client);
        var tools = sp.GetRequiredService<PullRequestTools>();

        // WI1: the real ResilientApiService maps a 404 read to a descriptive McpException.
        var ex = await Assert.ThrowsAsync<McpException>(() => tools.GetPullRequestDetailsAsync("PROJ", "repo", 1));
        Assert.Contains("Resource not found", ex.Message);
    }

    [Fact]
    public async Task GetPullRequest_RendersTimestampAsCanonicalUtc()
    {
        var created = new DateTimeOffset(2026, 6, 5, 14, 30, 0, TimeSpan.FromHours(2)); // 12:30 UTC
        var client = Substitute.For<IBitbucketClient>();
        client.GetPullRequestAsync("PROJ", "repo", 1, Arg.Any<CancellationToken>())
            .Returns(new PullRequest { Id = 1, CreatedDate = created, UpdatedDate = created });

        using var sp = BuildRealPipeline(client);
        var tools = sp.GetRequiredService<PullRequestTools>();

        var output = await tools.GetPullRequestDetailsAsync("PROJ", "repo", 1);

        // WI4: timestamps render in the single canonical UTC format from the real formatter.
        Assert.Contains("2026-06-05 12:30:00 UTC", output);
    }
}