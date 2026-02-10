using Bitbucket.Net;
using Serilog;
using Serilog.Events;
using StashMcpServer.Configuration;
using StashMcpServer.Formatting;
using StashMcpServer.Logging;
using StashMcpServer.Services;
using System.Net.Http.Headers;

namespace StashMcpServer.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register application services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string BitbucketHttpClientName = "BitbucketApi";

    /// <summary>
    /// Registers Bitbucket API services including the HTTP client, cache, resilient API, and diff formatter.
    /// </summary>
    public static IServiceCollection AddBitbucketServices(
        this IServiceCollection services,
        string stashUrl,
        string pat)
    {
        // Register named HttpClient with IHttpClientFactory for proper connection pooling
        // This avoids socket exhaustion and handles DNS changes properly
        services.AddHttpClient(BitbucketHttpClientName, client =>
        {
            client.BaseAddress = new Uri(stashUrl);
            client.Timeout = TimeSpan.FromMinutes(2); // Allow longer operations like large diffs
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Add memory cache for TTL-based caching of dynamic data
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1000; // Max number of cache entries
            options.CompactionPercentage = 0.25; // Remove 25% when limit reached
        });

        // Register BitbucketClient using the named HttpClient
        services.AddSingleton(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(BitbucketHttpClientName);
            return new BitbucketClient(httpClient, stashUrl, () => pat);
        });

        services.AddSingleton<IDiffFormatter, DiffFormatter>();

        // Register cache and resilient API services with interface mappings
        services.AddSingleton<BitbucketCacheService>();
        services.AddSingleton<IBitbucketCacheService>(sp => sp.GetRequiredService<BitbucketCacheService>());
        services.AddSingleton<ResilientApiService>();
        services.AddSingleton<IResilientApiService>(sp => sp.GetRequiredService<ResilientApiService>());

        // Load and register settings from environment variables
        var resilienceSettings = ResilienceSettings.FromEnvironment();
        services.AddSingleton(resilienceSettings);

        var serverSettings = ServerSettings.FromEnvironment();
        services.AddSingleton(serverSettings);
        services.AddSingleton<IServerSettings>(serverSettings);

        return services;
    }

    /// <summary>
    /// Configures Serilog logging with MCP transport bridge.
    /// Logs are routed through McpSerilogSink → McpLogQueue → McpLogDispatcher → MCP client.
    /// </summary>
    public static IServiceCollection AddMcpLogging(
        this IServiceCollection services,
        LogEventLevel logLevel)
    {
        var logQueue = new McpLogQueue();
        services.AddSingleton(logQueue);
        services.AddHostedService<McpLogDispatcher>();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .Enrich.FromLogContext()
            .WriteTo.Async(writeTo => writeTo.Sink(new McpSerilogSink(logQueue)))
            .CreateLogger();

        return services;
    }
}