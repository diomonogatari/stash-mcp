using Polly.CircuitBreaker;

namespace StashMcpServer.Services;

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