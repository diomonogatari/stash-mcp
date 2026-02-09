using Bitbucket.Net;
using StashMcpServer.Services;

namespace StashMcpServer.Tools;

/// <summary>
/// Abstract base class for all MCP tool classes.
/// Provides common dependencies and shared helper methods.
/// </summary>
public abstract class ToolBase(
    ILogger logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    BitbucketClient client,
    IServerSettings serverSettings)
{
    protected ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
    protected IBitbucketCacheService CacheService { get; } = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    protected IResilientApiService ResilientApi { get; } = resilientApi ?? throw new ArgumentNullException(nameof(resilientApi));
    protected BitbucketClient Client { get; } = client ?? throw new ArgumentNullException(nameof(client));
    protected IServerSettings ServerSettings { get; } = serverSettings ?? throw new ArgumentNullException(nameof(serverSettings));

    /// <summary>
    /// Checks if the server is in read-only mode and returns the error message if so.
    /// Returns null if write operations are allowed.
    /// </summary>
    protected string? CheckReadOnlyMode()
    {
        return ServerSettings.ReadOnlyMode ? Configuration.ServerSettings.ReadOnlyErrorMessage : null;
    }

    protected string NormalizeProjectKey(string projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            throw new ArgumentException("Project key is required.", nameof(projectKey));
        }

        var trimmedKey = projectKey.Trim();
        var cachedProject = CacheService.FindProject(trimmedKey);

        return cachedProject?.Key ?? trimmedKey;
    }

    protected string NormalizeRepositorySlug(string projectKey, string repositorySlug)
    {
        if (string.IsNullOrWhiteSpace(repositorySlug))
        {
            throw new ArgumentException("Repository slug is required.", nameof(repositorySlug));
        }

        var trimmedSlug = repositorySlug.Trim();
        var cachedRepository = CacheService.FindRepository(projectKey, trimmedSlug);

        return cachedRepository?.Slug ?? trimmedSlug;
    }

    protected void LogToolInvocation(string toolName, params (string Name, object? Value)[] parameters)
    {
        if (!Logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var formattedArguments = string.Join(", ", parameters.Select(p => $"{p.Name}={p.Value ?? "<null>"}"));
        Logger.LogDebug("Tool {ToolName} invoked with {ToolArguments}", toolName, formattedArguments);
    }
}