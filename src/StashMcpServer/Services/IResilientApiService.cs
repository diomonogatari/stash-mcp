using Polly.CircuitBreaker;

namespace StashMcpServer.Services;

/// <summary>
/// Cache lifetime category for a read operation. Lets call sites express intent
/// (e.g. "this is immutable, cache it long") instead of hard-coding TTLs.
/// </summary>
public enum CacheDuration
{
    /// <summary>Slow-changing / effectively-immutable data (commits, branch/tag lists, file content). Long TTL.</summary>
    Static,

    /// <summary>General dynamic data (PR lists, comments, dashboards). The standard TTL.</summary>
    Default,

    /// <summary>Fast-changing data (CI/build status). Short TTL.</summary>
    Short,
}

/// <summary>
/// Provides resilient API call execution with circuit breaker, retry, and graceful degradation.
/// </summary>
public interface IResilientApiService
{
    CircuitState CircuitState { get; }

    Task<T> ExecuteAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<T>> operation,
        TimeSpan? cacheTtl = null,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteWithoutCacheAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task ExecuteWithoutCacheAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    bool TryGetCached<T>(string cacheKey, out T? value);
    void InvalidateCache(string cacheKey);
    void InvalidatePullRequestCache(string projectKey, string repoSlug, long prId);
    void InvalidatePullRequestListCache(string projectKey, string repoSlug);
    void InvalidateAllContextVariations(string projectKey, string repoSlug, long prId);
}