using Bitbucket.Net.Common.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using StashMcpServer.Configuration;

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
        var effectiveTtl = cacheTtl ?? _settings.DynamicCacheTtl;

        try
        {
            var result = await _pipeline.ExecuteAsync(
                async ct => await operation(ct),
                cancellationToken);

            // Cache successful response with Size specified (required when SizeLimit is set)
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = effectiveTtl,
                Size = 1 // Each entry counts as 1 unit toward the SizeLimit
            };
            _cache.Set(cacheKey, result, cacheOptions);
            _logger.LogDebug("Cached result for key {CacheKey} with TTL {Ttl}s", cacheKey, effectiveTtl.TotalSeconds);

            return result;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning("Circuit breaker is open for key {CacheKey}. Attempting graceful degradation.", cacheKey);
            return HandleGracefulDegradation<T>(cacheKey, ex);
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning("Request timed out for key {CacheKey}. Attempting graceful degradation.", cacheKey);
            return HandleGracefulDegradation<T>(cacheKey, ex);
        }
        catch (BitbucketApiException ex)
        {
            _logger.LogError(ex, "Bitbucket API error for key {CacheKey}: {StatusCode}", cacheKey, (int)ex.StatusCode);
            throw new McpException($"Bitbucket API error ({(int)ex.StatusCode}): {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            _logger.LogError(ex, "API operation failed for key {CacheKey}", cacheKey);
            throw new McpException($"API request failed for '{cacheKey}': {ex.Message}");
        }
    }

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
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning("Circuit breaker is open for write operation. Error: {Message}", ex.Message);
            throw new McpException($"Bitbucket API unavailable (circuit breaker open): {ex.Message}");
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning("Write operation timed out. Error: {Message}", ex.Message);
            throw new McpException($"Operation timed out: {ex.Message}");
        }
        catch (BitbucketNotFoundException ex)
        {
            _logger.LogWarning("Resource not found: {Message}", ex.Message);
            throw new McpException($"Resource not found: {ex.Context ?? ex.Message}");
        }
        catch (BitbucketForbiddenException ex)
        {
            _logger.LogWarning("Access forbidden: {Message}", ex.Message);
            throw new McpException($"Access forbidden: {ex.Message}");
        }
        catch (BitbucketApiException ex)
        {
            _logger.LogError(ex, "Bitbucket API error ({StatusCode}): {Message}", (int)ex.StatusCode, ex.Message);
            throw new McpException($"Bitbucket API error ({(int)ex.StatusCode}): {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            _logger.LogError(ex, "Write operation failed unexpectedly");
            throw new McpException($"Operation failed: {ex.Message}");
        }
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
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning("Circuit breaker is open for write operation. Error: {Message}", ex.Message);
            throw new McpException($"Bitbucket API unavailable (circuit breaker open): {ex.Message}");
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning("Write operation timed out. Error: {Message}", ex.Message);
            throw new McpException($"Operation timed out: {ex.Message}");
        }
        catch (BitbucketNotFoundException ex)
        {
            _logger.LogWarning("Resource not found: {Message}", ex.Message);
            throw new McpException($"Resource not found: {ex.Context ?? ex.Message}");
        }
        catch (BitbucketForbiddenException ex)
        {
            _logger.LogWarning("Access forbidden: {Message}", ex.Message);
            throw new McpException($"Access forbidden: {ex.Message}");
        }
        catch (BitbucketApiException ex)
        {
            _logger.LogError(ex, "Bitbucket API error ({StatusCode}): {Message}", (int)ex.StatusCode, ex.Message);
            throw new McpException($"Bitbucket API error ({(int)ex.StatusCode}): {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            _logger.LogError(ex, "Write operation failed unexpectedly");
            throw new McpException($"Operation failed: {ex.Message}");
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
    /// Invalidates a cache entry.
    /// </summary>
    /// <param name="cacheKey">The exact cache key to invalidate.</param>
    public void InvalidateCache(string cacheKey)
    {
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache for key {CacheKey}", cacheKey);
    }

    /// <summary>
    /// Invalidates all cache entries related to a specific pull request.
    /// This is a convenience method that invalidates PR details, context, comments, activities, tasks, and diff.
    /// </summary>
    /// <param name="projectKey">The Bitbucket project key.</param>
    /// <param name="repoSlug">The repository slug.</param>
    /// <param name="prId">The pull request ID.</param>
    public void InvalidatePullRequestCache(string projectKey, string repoSlug, long prId)
    {
        InvalidateCache(CacheKeys.PullRequest(projectKey, repoSlug, prId));
        InvalidateCache(CacheKeys.PullRequestDetails(projectKey, repoSlug, prId));
        InvalidateCache(CacheKeys.PullRequestComments(projectKey, repoSlug, prId));
        InvalidateCache(CacheKeys.PullRequestActivities(projectKey, repoSlug, prId));
        InvalidateCache(CacheKeys.PullRequestTasks(projectKey, repoSlug, prId));
        InvalidateCache(CacheKeys.PullRequestDiff(projectKey, repoSlug, prId));
        InvalidateCache(CacheKeys.PullRequestChanges(projectKey, repoSlug, prId));

        // Invalidate all context variations (16 combinations of 4 boolean flags)
        InvalidateAllContextVariations(projectKey, repoSlug, prId);

        _logger.LogInformation("Invalidated all cache entries for PR #{PrId} in {ProjectKey}/{RepoSlug}", prId, projectKey, repoSlug);
    }

    /// <summary>
    /// Invalidates the pull request list cache for a repository.
    /// This invalidates all state variations (OPEN, MERGED, DECLINED, ALL).
    /// </summary>
    /// <param name="projectKey">The Bitbucket project key.</param>
    /// <param name="repoSlug">The repository slug.</param>
    public void InvalidatePullRequestListCache(string projectKey, string repoSlug)
    {
        foreach (var state in new[] { "OPEN", "MERGED", "DECLINED", "ALL" })
        {
            InvalidateCache(CacheKeys.PullRequestList(projectKey, repoSlug, state));
        }

        _logger.LogInformation("Invalidated PR list cache for {ProjectKey}/{RepoSlug}", projectKey, repoSlug);
    }

    /// <summary>
    /// Invalidates all context cache variations for a pull request.
    /// This covers all 16 combinations of the 4 boolean flags (comments, diff, activity, tasks).
    /// </summary>
    /// <param name="projectKey">The Bitbucket project key.</param>
    /// <param name="repoSlug">The repository slug.</param>
    /// <param name="prId">The pull request ID.</param>
    public void InvalidateAllContextVariations(string projectKey, string repoSlug, long prId)
    {
        foreach (var includeComments in new[] { true, false })
        {
            foreach (var includeDiff in new[] { true, false })
            {
                foreach (var includeActivity in new[] { true, false })
                {
                    foreach (var includeTasks in new[] { true, false })
                    {
                        InvalidateCache(CacheKeys.PullRequestContext(projectKey, repoSlug, prId, includeComments, includeDiff, includeActivity, includeTasks));
                    }
                }
            }
        }
    }

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

        // Layer 2: Retry with exponential backoff
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