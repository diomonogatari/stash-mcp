using Bitbucket.Net;
using StashMcpServer.Configuration;

namespace StashMcpServer.Services;

/// <summary>
/// Performs deferred startup tasks after the MCP server is running.
/// This ensures connection validation and cache initialization happen
/// after the MCP client has connected and set a logging level,
/// making startup logs visible in VS Code's MCP output panel.
/// </summary>
internal sealed class StartupService(
    BitbucketClient bitbucketClient,
    BitbucketCacheService cacheService,
    ResilienceSettings resilienceSettings,
    ServerSettings serverSettings,
    ILogger<StartupService> logger,
    IHostApplicationLifetime hostLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to allow MCP initialization and logging/setLevel to complete
        // The MCP client typically sends logging/setLevel immediately after initialize
        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);

        try
        {
            await ValidateConnectionAsync(stoppingToken).ConfigureAwait(false);
            await InitializeCacheAsync(stoppingToken).ConfigureAwait(false);
            LogStartupInfo();

            await WarmupDefaultBranchesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown, don't log error
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Startup failed: {Message}", ex.Message);
            hostLifetime.StopApplication();
        }
    }

    private async Task ValidateConnectionAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Validating connection to Bitbucket Server...");

        try
        {
            await bitbucketClient.GetProjectsAsync(limit: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Connection to Bitbucket Server validated successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to Bitbucket Server. Check BITBUCKET_URL and BITBUCKET_TOKEN.");
            throw;
        }
    }

    private async Task InitializeCacheAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing cache...");
        await cacheService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Cache initialized successfully.");
    }

    private async Task WarmupDefaultBranchesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cacheService.WarmupDefaultBranchesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown during warmup — not an error
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background default branch warmup failed. Branches will be fetched on demand.");
        }
    }

    private void LogStartupInfo()
    {
        if (serverSettings.ReadOnlyMode)
        {
            logger.LogWarning("MCP Server started in READ-ONLY mode. Write operations are disabled.");
        }

        logger.LogInformation(
            "Resilience settings: MaxRetry={MaxRetry}, CircuitTimeout={CircuitTimeout}s, CacheTTL={CacheTTL}s",
            resilienceSettings.MaxRetryAttempts,
            resilienceSettings.CircuitBreakerBreakDuration.TotalSeconds,
            resilienceSettings.DynamicCacheTtl.TotalSeconds);

        var currentMemory = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        var gcMemory = GC.GetTotalMemory(false);
        logger.LogInformation(
            "Memory Usage - Working Set: {Memory:F2} MB, GC: {GcMemory:F2} MB",
            currentMemory / 1024.0 / 1024.0,
            gcMemory / 1024.0 / 1024.0);

        logger.LogInformation("Stash MCP Server is ready.");
    }
}