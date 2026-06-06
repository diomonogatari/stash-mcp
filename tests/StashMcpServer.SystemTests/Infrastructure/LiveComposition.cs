using Microsoft.Extensions.DependencyInjection;
using StashMcpServer.Extensions;
using StashMcpServer.Services;
using StashMcpServer.Tools;

namespace StashMcpServer.SystemTests.Infrastructure;

/// <summary>
/// Builds the real service pipeline (cache, resilience, formatter, HTTP client) pointed at a live
/// Bitbucket container and resolves the concrete tool classes directly — no MCP transport. This is
/// the deterministic backbone for the end-to-end tests: the cache is warmed synchronously before the
/// provider is handed back, so cache-backed tools (e.g. <c>list_projects</c>) see seeded data.
/// </summary>
internal static class LiveComposition
{
    /// <summary>Builds the provider and warms the cache. Dispose the returned provider when done.</summary>
    public static async Task<ServiceProvider> BuildAsync(Uri baseUrl, string accessToken, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBitbucketServices(baseUrl.ToString(), accessToken);

        // Register the concrete tool classes under test (the MCP builder does this in production).
        services.AddSingleton<ProjectTools>();
        services.AddSingleton<RepositoryTools>();
        services.AddSingleton<PullRequestTools>();
        services.AddSingleton<DashboardTools>();
        services.AddSingleton<GitTools>();
        services.AddSingleton<HistoryTools>();
        services.AddSingleton<BuildTools>();
        services.AddSingleton<SearchTools>();

        var provider = services.BuildServiceProvider();

        // Warm the cache exactly as StartupService does, but synchronously and deterministically.
        await provider.GetRequiredService<BitbucketCacheService>().InitializeAsync(cancellationToken).ConfigureAwait(false);

        return provider;
    }
}