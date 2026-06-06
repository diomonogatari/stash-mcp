using Microsoft.Extensions.DependencyInjection;
using StashMcpServer.Extensions;
using StashMcpServer.Services;
using StashMcpServer.Tools;

namespace StashMcpServer.SystemTests.Infrastructure;

/// <summary>
/// Boots the MCP server in-process over the real composition root (<see cref="ServiceCollectionExtensions.AddBitbucketServices"/>)
/// pointed at a live Bitbucket container, and connects a real MCP client over in-memory pipes.
/// Unlike <c>StashMcpTestFactory</c> (which substitutes the HTTP boundary), nothing is mocked here.
///
/// The production <c>StartupService</c> warms the project cache as a background task, which races
/// tool calls in a test. Instead, call <see cref="WarmCacheAsync"/> once after creating the client
/// to populate the cache deterministically — mirroring exactly what startup does.
/// </summary>
public sealed class LiveStashMcpFactory(Uri baseUrl, string accessToken)
    : McpServerFactory.Testing.McpServerFactory(configureServices: null, configureMcpServer: null, options: null)
{
    protected override void ConfigureMcpServer(IMcpServerBuilder builder) =>
        builder.WithToolsFromAssembly(typeof(ProjectTools).Assembly);

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddBitbucketServices(baseUrl.ToString(), accessToken);

    /// <summary>
    /// Populates the project/repository cache from the live server, the same work
    /// <c>StartupService</c> performs. Call after <c>CreateClientAsync</c> so the host is built.
    /// </summary>
    public Task WarmCacheAsync(CancellationToken cancellationToken = default) =>
        Services.GetRequiredService<BitbucketCacheService>().InitializeAsync(cancellationToken);
}