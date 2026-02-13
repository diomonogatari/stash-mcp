using Bitbucket.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StashMcpServer.Formatting;
using StashMcpServer.Services;
using StashMcpServer.Tools;

namespace StashMcpServer.IntegrationTests;

public sealed class StashMcpTestFactory(Action<StashMcpTestFactory>? configureMocks = null)
    : McpServerFactory.Testing.McpServerFactory(
        configureServices: null,
        configureMcpServer: null,
        options: null)
{
    public IBitbucketClient BitbucketClient { get; } = Substitute.For<IBitbucketClient>();

    public IBitbucketCacheService CacheService { get; } = Substitute.For<IBitbucketCacheService>();

    public IResilientApiService ResilientApi { get; } = Substitute.For<IResilientApiService>();

    public IServerSettings ServerSettings { get; } = Substitute.For<IServerSettings>();

    public IDiffFormatter DiffFormatter { get; } = Substitute.For<IDiffFormatter>();

    protected override void ConfigureMcpServer(IMcpServerBuilder builder)
    {
        builder.WithToolsFromAssembly(typeof(ProjectTools).Assembly);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        ServerSettings.ReadOnlyMode.Returns(false);
        ServerSettings.Projects.Returns(Array.Empty<string>());

        configureMocks?.Invoke(this);

        RemoveStartupServiceDescriptors(services);

        services.AddSingleton(BitbucketClient);
        services.AddSingleton(CacheService);
        services.AddSingleton(ResilientApi);
        services.AddSingleton(ServerSettings);
        services.AddSingleton(DiffFormatter);
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
    }

    private static void RemoveStartupServiceDescriptors(IServiceCollection services)
    {
        var descriptorsToRemove = services
            .Where(descriptor =>
                descriptor.ImplementationType?.Name == "StartupService" ||
                descriptor.ImplementationInstance?.GetType().Name == "StartupService")
            .ToArray();

        foreach (var descriptor in descriptorsToRemove)
        {
            services.Remove(descriptor);
        }
    }
}
