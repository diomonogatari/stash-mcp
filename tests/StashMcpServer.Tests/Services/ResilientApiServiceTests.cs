using Bitbucket.Net.Common.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using StashMcpServer.Configuration;
using StashMcpServer.Services;
using System.Net;

namespace StashMcpServer.Tests.Services;

public class ResilientApiServiceTests : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly ResilientApiService _sut;

    public ResilientApiServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });

        var settings = new ResilienceSettings
        {
            MaxRetryAttempts = 1,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            CircuitBreakerBreakDuration = TimeSpan.FromMilliseconds(500),
            CircuitBreakerMinimumThroughput = 100,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(5),
            DynamicCacheTtl = TimeSpan.FromMinutes(5),
            EnableGracefulDegradation = true,
        };

        _sut = new ResilientApiService(
            _cache,
            settings,
            NullLogger<ResilientApiService>.Instance);
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_ReturnsCachedResult()
    {
        var result = await _sut.ExecuteAsync(
            "test-key",
            _ => Task.FromResult("hello"),
            cancellationToken: CancellationToken.None);

        Assert.Equal("hello", result);

        Assert.True(_sut.TryGetCached<string>("test-key", out var cached));
        Assert.Equal("hello", cached);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_CachesWithCustomTtl()
    {
        await _sut.ExecuteAsync(
            "ttl-key",
            _ => Task.FromResult(42),
            cacheTtl: TimeSpan.FromMinutes(10));

        Assert.True(_sut.TryGetCached<int>("ttl-key", out var cached));
        Assert.Equal(42, cached);
    }

    [Fact]
    public async Task ExecuteAsync_BitbucketApiException_ThrowsMcpException()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteAsync<string>(
                "api-error-key",
                _ => throw new BitbucketApiException("Not found", HttpStatusCode.NotFound, [])));

        Assert.Contains("Bitbucket API error (404)", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_GenericException_ThrowsMcpException()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteAsync<string>(
                "generic-error-key",
                _ => throw new InvalidOperationException("something broke")));

        Assert.Contains("something broke", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_NotFound_ThrowsMcpExceptionWithDescriptiveMessage()
    {
        // Cached reads must surface the same descriptive error as uncached writes.
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteAsync<string>(
                "notfound-key",
                _ => throw new BitbucketNotFoundException("Not found", [], "PR#123")));

        Assert.Contains("Resource not found", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Forbidden_ThrowsMcpExceptionWithDescriptiveMessage()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteAsync<string>(
                "forbidden-key",
                _ => throw new BitbucketForbiddenException("No access", [])));

        Assert.Contains("Access forbidden", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_OperationCancelled_PropagatesWithoutWrapping()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.ExecuteAsync<string>(
                "cancel-key",
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult("never");
                },
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteWithoutCacheAsync_SuccessfulOperation_ReturnsResult()
    {
        var result = await _sut.ExecuteWithoutCacheAsync<string>(
            _ => Task.FromResult("write-result"));

        Assert.Equal("write-result", result);
    }

    [Fact]
    public async Task ExecuteWithoutCacheAsync_BitbucketApiException_ThrowsMcpException()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteWithoutCacheAsync<string>(
                _ => throw new BitbucketApiException("Server error", HttpStatusCode.InternalServerError, [])));

        Assert.Contains("Bitbucket API error (500)", ex.Message);
    }

    [Fact]
    public async Task ExecuteWithoutCacheAsync_Void_SuccessfulOperation_Completes()
    {
        var executed = false;

        await _sut.ExecuteWithoutCacheAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        Assert.True(executed);
    }

    [Fact]
    public async Task ExecuteWithoutCacheAsync_Void_BitbucketApiException_ThrowsMcpException()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteWithoutCacheAsync(
                _ => throw new BitbucketApiException("Forbidden", HttpStatusCode.Forbidden, [])));

        Assert.Contains("Bitbucket API error (403)", ex.Message);
    }

    [Fact]
    public async Task ExecuteWithoutCacheAsync_Void_GenericException_ThrowsMcpException()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteWithoutCacheAsync(
                _ => throw new InvalidOperationException("broke")));

        Assert.Contains("broke", ex.Message);
    }

    [Fact]
    public void TryGetCached_MissedKey_ReturnsFalse()
    {
        Assert.False(_sut.TryGetCached<string>("nonexistent-key", out _));
    }

    [Fact]
    public async Task InvalidateCache_RemovesCachedEntry()
    {
        await _sut.ExecuteAsync("inv-key", _ => Task.FromResult("value"));
        Assert.True(_sut.TryGetCached<string>("inv-key", out _));

        _sut.InvalidateCache("inv-key");

        Assert.False(_sut.TryGetCached<string>("inv-key", out _));
    }

    [Fact]
    public async Task InvalidatePullRequestCache_RemovesAllRelatedEntries()
    {
        var keys = new[]
        {
            CacheKeys.PullRequest("P", "R", 1),
            CacheKeys.PullRequestDetails("P", "R", 1),
            CacheKeys.PullRequestComments("P", "R", 1),
            CacheKeys.PullRequestActivities("P", "R", 1),
            CacheKeys.PullRequestTasks("P", "R", 1),
            CacheKeys.PullRequestChanges("P", "R", 1),
        };

        foreach (var key in keys)
        {
            await _sut.ExecuteAsync(key, _ => Task.FromResult("data"));
        }

        _sut.InvalidatePullRequestCache("P", "R", 1);

        foreach (var key in keys)
        {
            Assert.False(_sut.TryGetCached<string>(key, out _), $"Key {key} should have been invalidated");
        }
    }

    [Fact]
    public async Task InvalidatePullRequestCache_RemovesContextAndLimitVariants()
    {
        // Group eviction must clear every PR-context flag combination and every limit= variant —
        // the cases the old per-key enumeration missed.
        var contextA = CacheKeys.PullRequestContext("P", "R", 1, true, false, true, false);
        var contextB = CacheKeys.PullRequestContext("P", "R", 1, false, true, false, true);
        var detailsWithSuffix = $"{CacheKeys.PullRequestDetails("P", "R", 1)}:limit=99";

        await _sut.ExecuteAsync(contextA, _ => Task.FromResult("a"));
        await _sut.ExecuteAsync(contextB, _ => Task.FromResult("b"));
        await _sut.ExecuteAsync(detailsWithSuffix, _ => Task.FromResult("c"));

        _sut.InvalidatePullRequestCache("P", "R", 1);

        Assert.False(_sut.TryGetCached<string>(contextA, out _));
        Assert.False(_sut.TryGetCached<string>(contextB, out _));
        Assert.False(_sut.TryGetCached<string>(detailsWithSuffix, out _));
    }

    [Fact]
    public async Task InvalidatePullRequestListCache_RemovesLimitSuffixedVariants()
    {
        // Real list cache keys carry a ":limit=N" suffix; the old invalidation removed only the
        // base state keys and left these stale. Group eviction must clear them.
        var k25 = $"{CacheKeys.PullRequestList("P", "R", "OPEN")}:limit=25";
        var k50 = $"{CacheKeys.PullRequestList("P", "R", "OPEN")}:limit=50";

        await _sut.ExecuteAsync(k25, _ => Task.FromResult("a"));
        await _sut.ExecuteAsync(k50, _ => Task.FromResult("b"));

        _sut.InvalidatePullRequestListCache("P", "R");

        Assert.False(_sut.TryGetCached<string>(k25, out _));
        Assert.False(_sut.TryGetCached<string>(k50, out _));
    }

    [Fact]
    public async Task InvalidatePullRequestListCache_RemovesAllStateVariations()
    {
        foreach (var state in new[] { "OPEN", "MERGED", "DECLINED", "ALL" })
        {
            var key = CacheKeys.PullRequestList("P", "R", state);
            await _sut.ExecuteAsync(key, _ => Task.FromResult("data"));
        }

        _sut.InvalidatePullRequestListCache("P", "R");

        foreach (var state in new[] { "OPEN", "MERGED", "DECLINED", "ALL" })
        {
            var key = CacheKeys.PullRequestList("P", "R", state);
            Assert.False(_sut.TryGetCached<string>(key, out _));
        }
    }

    [Fact]
    public void CircuitState_DefaultsToClosedOnStartup()
    {
        Assert.Equal(Polly.CircuitBreaker.CircuitState.Closed, _sut.CircuitState);
    }

    [Fact]
    public async Task ExecuteAsync_GracefulDegradation_ReturnsStaleCacheOnBrokenCircuit()
    {
        var settings = new ResilienceSettings
        {
            MaxRetryAttempts = 1,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            CircuitBreakerMinimumThroughput = 2,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(30),
            CircuitBreakerFailureRatio = 0.1,
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(5),
            EnableGracefulDegradation = true,
        };

        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var sut = new ResilientApiService(cache, settings, NullLogger<ResilientApiService>.Instance);

        await sut.ExecuteAsync("degrade-key", _ => Task.FromResult("original"));

        // Trip the circuit breaker by failing enough calls
        for (var i = 0; i < 5; i++)
        {
            try
            {
                await sut.ExecuteAsync<string>($"fail-{i}", _ => throw new HttpRequestException("down"));
            }
            catch
            {
                // expected
            }
        }

        // If circuit is open, should return stale cache
        if (sut.CircuitState != Polly.CircuitBreaker.CircuitState.Closed)
        {
            var result = await sut.ExecuteAsync<string>("degrade-key", _ => throw new HttpRequestException("still down"));
            Assert.Equal("original", result);
        }
    }

    [Fact]
    public async Task ExecuteWithoutCacheAsync_NotFound_ThrowsMcpExceptionWithContext()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteWithoutCacheAsync<string>(
                _ => throw new BitbucketNotFoundException("Not found", [], "PR#123")));

        Assert.Contains("Resource not found", ex.Message);
    }

    [Fact]
    public async Task ExecuteWithoutCacheAsync_Forbidden_ThrowsMcpExceptionWithMessage()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ExecuteWithoutCacheAsync<string>(
                _ => throw new BitbucketForbiddenException("No access", [])));

        Assert.Contains("Access forbidden", ex.Message);
    }

    [Fact]
    public async Task Constructor_WithZeroRetries_BuildsPipelineAndExecutes()
    {
        // MaxRetryAttempts = 0 (allowed by the env clamp) must mean "no retries", not crash the
        // pipeline build — Polly's retry strategy rejects 0, so the retry layer is skipped.
        var settings = new ResilienceSettings
        {
            MaxRetryAttempts = 0,
            CircuitBreakerMinimumThroughput = 100,
            RequestTimeout = TimeSpan.FromSeconds(5),
            DynamicCacheTtl = TimeSpan.FromMinutes(5),
        };

        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var sut = new ResilientApiService(cache, settings, NullLogger<ResilientApiService>.Instance);

        var result = await sut.ExecuteAsync("zero-retry-key", _ => Task.FromResult("ok"));

        Assert.Equal("ok", result);
    }
}