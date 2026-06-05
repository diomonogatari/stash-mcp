using Bitbucket.Net.Common.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using ModelContextProtocol;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using StashMcpServer.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace StashMcpServer.Services;

/// <summary>
/// Provides resilient API call execution with circuit breaker, retry, and graceful degradation.
/// Wraps arbitrary async operations with Polly resilience policies.
/// </summary>
public class ResilientApiService : IResilientApiService
{
    private readonly IMemoryCache _cache;
    private readonly ResilienceSettings _settings;
    private readonly ILogger<ResilientApiService> _logger;
    private readonly ResiliencePipeline _pipeline;

    // One cancellation source per logical cache group (e.g. all entries of a PR). Cached entries
    // register the group's change token; invalidating the group trips the token, evicting every
    // variant atomically. See CacheGroups.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _groupTokens = new(StringComparer.Ordinal);

    // Track circuit state for logging
    private CircuitState _lastCircuitState = CircuitState.Closed;

    public ResilientApiService(
        IMemoryCache cache,
        ResilienceSettings settings,
        ILogger<ResilientApiService> logger)
    {
        _cache = cache;
        _settings = settings;
        _logger = logger;
        _pipeline = BuildResiliencePipeline();
    }

    /// <summary>
    /// Gets the current circuit breaker state.
    /// </summary>
    public CircuitState CircuitState => _lastCircuitState;

    /// <summary>
    /// Executes an async operation with resilience (retry, circuit breaker, timeout).
    /// Supports graceful degradation by returning cached data when the circuit is open.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="cacheKey">Cache key for storing/retrieving results.</param>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="cacheTtl">Optional custom TTL for caching. Defaults to DynamicCacheTtl.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation, or cached data if circuit is open.</returns>
    public async Task<T> ExecuteAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<T>> operation,
        TimeSpan? cacheTtl = null,
        CancellationToken cancellationToken = default)
    {
        // Per-category TTL: slow-changing data (commits, branches, file content) is cached
        // longer and fast-changing data (CI/build status) shorter, derived from the key prefix.
        var effectiveTtl = cacheTtl ?? ResolveTtl(CachePolicy.Categorize(cacheKey));

        try
        {
            var result = await _pipeline.ExecuteAsync(
                async ct => await operation(ct),
                cancellationToken);

            // Cache successful response. Size is required when a SizeLimit is set; weight larger
            // payloads (strings, collections) more so they count proportionally toward the budget.
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = effectiveTtl,
                Size = CacheEntrySize.Estimate(result),
            };

            // Attach the key's invalidation group (if any) so a single write can evict all of its
            // variants — every limit= suffix and every PR-context flag combination — atomically.
            var invalidationGroup = CacheGroups.GroupFor(cacheKey);
            if (invalidationGroup is not null)
            {
                cacheOptions.AddExpirationToken(GetGroupChangeToken(invalidationGroup));
            }

            _cache.Set(cacheKey, result, cacheOptions);
            _logger.LogDebug("Cached result for key {CacheKey} with TTL {Ttl}s", cacheKey, effectiveTtl.TotalSeconds);

            return result;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker is open for key {CacheKey}. Attempting graceful degradation.", cacheKey);
            return HandleGracefulDegradation<T>(cacheKey, ex);
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(ex, "Request timed out for key {CacheKey}. Attempting graceful degradation.", cacheKey);
            return HandleGracefulDegradation<T>(cacheKey, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            // Route through the shared mapper so cached reads surface the same
            // descriptive errors (not-found / forbidden / server) as uncached writes.
            throw MapApiException(ex);
        }
    }

    private TimeSpan ResolveTtl(CacheDuration duration) => duration switch
    {
        CacheDuration.Static => _settings.StaticCacheTtl,
        CacheDuration.Short => _settings.ShortCacheTtl,
        _ => _settings.DynamicCacheTtl,
    };

    /// <summary>
    /// Executes an async operation with resilience but without caching.
    /// Use this for write operations or one-time fetches.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<T> ExecuteWithoutCacheAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(
                async ct => await operation(ct),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            ThrowUncachedException(ex);
        }

        return default!; // Unreachable — ThrowUncachedException always throws
    }

    /// <summary>
    /// Executes an async operation with resilience but without caching.
    /// Use this for write operations that don't return a value.
    /// </summary>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteWithoutCacheAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _pipeline.ExecuteAsync(
                async ct =>
                {
                    await operation(ct);
                    return true;
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            ThrowUncachedException(ex);
        }
    }

    /// <summary>
    /// Tries to get a cached value without making an API call.
    /// Useful for checking if stale data is available before making expensive calls.
    /// </summary>
    public bool TryGetCached<T>(string cacheKey, out T? value)
    {
        return _cache.TryGetValue(cacheKey, out value);
    }

    /// <summary>
    /// Invalidates a single cache entry by its exact key.
    /// </summary>
    /// <param name="cacheKey">The exact cache key to invalidate.</param>
    public void InvalidateCache(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache for key {CacheKey}", cacheKey);
    }

    /// <summary>
    /// Invalidates every cache entry belonging to a specific pull request (details, context,
    /// comments, activities, tasks, changes, merge-base, Jira links) in one atomic group eviction.
    /// </summary>
    public void InvalidatePullRequestCache(string projectKey, string repoSlug, long prId)
    {
        InvalidateGroup(CacheGroups.PrItemGroup(projectKey, repoSlug, prId));
        _logger.LogInformation("Invalidated all cache entries for PR #{PrId} in {ProjectKey}/{RepoSlug}", prId, projectKey, repoSlug);
    }

    /// <summary>
    /// Invalidates the pull-request list views of a repository — all states (OPEN/MERGED/DECLINED/ALL)
    /// and all <c>limit=</c> variants — in one atomic group eviction.
    /// </summary>
    public void InvalidatePullRequestListCache(string projectKey, string repoSlug)
    {
        InvalidateGroup(CacheGroups.PrListGroup(projectKey, repoSlug));
        _logger.LogInformation("Invalidated PR list cache for {ProjectKey}/{RepoSlug}", projectKey, repoSlug);
    }

    /// <summary>
    /// Invalidates all PR-context cache variations for a pull request. Context entries live in the
    /// PR-item group, so this evicts every flag combination in one group eviction.
    /// </summary>
    public void InvalidateAllContextVariations(string projectKey, string repoSlug, long prId)
    {
        InvalidateGroup(CacheGroups.PrItemGroup(projectKey, repoSlug, prId));
    }

    /// <summary>
    /// Returns the change token for a cache group, creating the group's cancellation source on
    /// first use. Tolerates a concurrent invalidation disposing the source between lookups.
    /// </summary>
    private CancellationChangeToken GetGroupChangeToken(string group)
    {
        while (true)
        {
            var cts = _groupTokens.GetOrAdd(group, static _ => new CancellationTokenSource());
            try
            {
                return new CancellationChangeToken(cts.Token);
            }
            catch (ObjectDisposedException)
            {
                // A concurrent InvalidateGroup disposed this source; drop the stale entry and retry.
                _groupTokens.TryRemove(new KeyValuePair<string, CancellationTokenSource>(group, cts));
            }
        }
    }

    /// <summary>
    /// Trips a cache group's change token, evicting all entries registered under it, and replaces
    /// the source so future entries start a fresh group.
    /// </summary>
    private void InvalidateGroup(string group)
    {
        if (_groupTokens.TryRemove(group, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }

            _logger.LogDebug("Invalidated cache group {Group}", group);
        }
    }

    /// <summary>
    /// Maps a known API/resilience exception to a descriptive <see cref="McpException"/>,
    /// logging at an appropriate level. Shared by both the cached and uncached execution
    /// paths so reads and writes surface identical, deterministic error messages.
    /// </summary>
    private McpException MapApiException(Exception ex)
    {
        switch (ex)
        {
            case BrokenCircuitException bce:
                _logger.LogWarning("Circuit breaker is open. Error: {Message}", bce.Message);
                return new McpException($"Bitbucket API unavailable (circuit breaker open): {bce.Message}");

            case TimeoutRejectedException tre:
                _logger.LogWarning("Operation timed out. Error: {Message}", tre.Message);
                return new McpException($"Operation timed out: {tre.Message}");

            case BitbucketNotFoundException bnfe:
                _logger.LogWarning("Resource not found: {Message}", bnfe.Message);
                return new McpException($"Resource not found: {bnfe.Context ?? bnfe.Message}");

            case BitbucketForbiddenException bfe:
                _logger.LogWarning("Access forbidden: {Message}", bfe.Message);
                return new McpException($"Access forbidden: {bfe.Message}");

            case BitbucketApiException bae:
                _logger.LogError(ex, "Bitbucket API error ({StatusCode}): {Message}", (int)bae.StatusCode, bae.Message);
                return new McpException($"Bitbucket API error ({(int)bae.StatusCode}): {bae.Message}");

            default:
                _logger.LogError(ex, "Operation failed unexpectedly");
                return new McpException($"Operation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps and throws a known API exception as an <see cref="McpException"/>.
    /// Thin throwing wrapper over <see cref="MapApiException"/> for uncached call sites.
    /// </summary>
    [DoesNotReturn]
    private void ThrowUncachedException(Exception ex) => throw MapApiException(ex);

    private T HandleGracefulDegradation<T>(string cacheKey, Exception originalException)
    {
        if (!_settings.EnableGracefulDegradation)
        {
            _logger.LogWarning("Graceful degradation disabled. Rethrowing original exception.");
            throw new McpException($"API request failed: {originalException.Message}");
        }

        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            _logger.LogInformation(
                "Returning stale cached data for key {CacheKey} due to: {ExceptionType}",
                cacheKey,
                originalException.GetType().Name);
            return cached;
        }

        _logger.LogError(
            originalException,
            "No cached data available for key {CacheKey}. Service unavailable.",
            cacheKey);

        throw new McpException(
            $"Bitbucket API unavailable and no cached data for '{cacheKey}'. Error: {originalException.Message}");
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        var builder = new ResiliencePipelineBuilder();

        // Layer 1: Timeout (innermost - applied to each attempt)
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = _settings.RequestTimeout,
            OnTimeout = args =>
            {
                _logger.LogWarning(
                    "Request timed out after {Timeout}s",
                    args.Timeout.TotalSeconds);
                return default;
            }
        });

        // Layer 2: Retry with exponential backoff. Skipped when retries are disabled
        // (MaxRetryAttempts == 0), because Polly's retry strategy requires at least one attempt
        // and would otherwise throw a validation error while building the pipeline.
        if (_settings.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _settings.MaxRetryAttempts,
                Delay = _settings.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<BitbucketRateLimitException>()       // 429 - always retry with backoff
                    .Handle<BitbucketServerException>(),        // 5xx - server errors are often transient
                OnRetry = args =>
                {
                    var exceptionDetails = args.Outcome.Exception switch
                    {
                        BitbucketApiException bbEx => $"{bbEx.GetType().Name} ({(int)bbEx.StatusCode}): {bbEx.Message}",
                        _ => args.Outcome.Exception?.Message ?? "Unknown"
                    };
                    _logger.LogWarning(
                        "Retry attempt {AttemptNumber}/{MaxRetries} after {Delay}ms due to: {ExceptionMessage}",
                        args.AttemptNumber,
                        _settings.MaxRetryAttempts,
                        args.RetryDelay.TotalMilliseconds,
                        exceptionDetails);
                    return default;
                }
            });
        }

        // Layer 3: Circuit breaker (outermost)
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = _settings.CircuitBreakerFailureRatio,
            SamplingDuration = _settings.CircuitBreakerSamplingDuration,
            MinimumThroughput = _settings.CircuitBreakerMinimumThroughput,
            BreakDuration = _settings.CircuitBreakerBreakDuration,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>()
                .Handle<BitbucketServerException>(),        // 5xx - server errors indicate server health issues
            OnOpened = args =>
            {
                _lastCircuitState = CircuitState.Open;
                var exceptionDetails = args.Outcome.Exception switch
                {
                    BitbucketApiException bbEx => $"{bbEx.GetType().Name} ({(int)bbEx.StatusCode}): {bbEx.Message}",
                    _ => args.Outcome.Exception?.Message ?? "Unknown"
                };
                _logger.LogWarning(
                    "Circuit breaker OPENED. Break duration: {BreakDuration}s. Reason: {Exception}",
                    args.BreakDuration.TotalSeconds,
                    exceptionDetails);
                return default;
            },
            OnClosed = args =>
            {
                _lastCircuitState = CircuitState.Closed;
                _logger.LogInformation("Circuit breaker CLOSED. Normal operation resumed.");
                return default;
            },
            OnHalfOpened = args =>
            {
                _lastCircuitState = CircuitState.HalfOpen;
                _logger.LogInformation("Circuit breaker HALF-OPEN. Testing if service recovered.");
                return default;
            }
        });

        return builder.Build();
    }
}