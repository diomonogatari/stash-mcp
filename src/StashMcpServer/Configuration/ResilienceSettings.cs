namespace StashMcpServer.Configuration;

/// <summary>
/// Configuration settings for Bitbucket API resilience (circuit breaker, retry, timeout).
/// </summary>
public class ResilienceSettings
{
    /// <summary>
    /// Maximum number of retry attempts for transient failures.
    /// Default: 3
    /// Environment variable: BITBUCKET_RETRY_COUNT
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay between retry attempts (used with exponential backoff).
    /// Default: 500ms
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Circuit breaker failure ratio threshold before opening the circuit.
    /// Default: 0.5 (50% failure rate)
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Duration over which failure ratio is calculated.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum number of calls before circuit breaker can trip.
    /// Default: 10
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// How long the circuit stays open before transitioning to half-open.
    /// Default: 30 seconds
    /// Environment variable: BITBUCKET_CIRCUIT_TIMEOUT
    /// </summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for individual API requests.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// TTL for cached dynamic data (PR lists, comments) — the default category.
    /// Default: 60 seconds
    /// Environment variable: BITBUCKET_CACHE_TTL_SECONDS
    /// </summary>
    public TimeSpan DynamicCacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// TTL for slow-changing / effectively-immutable data (commit-by-hash content and
    /// changes, branch/tag lists, file content). Cached longer to cut redundant API calls
    /// and keep output stable within a session.
    /// Default: 10 minutes
    /// Environment variable: BITBUCKET_CACHE_TTL_STATIC_SECONDS
    /// </summary>
    public TimeSpan StaticCacheTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// TTL for fast-changing data (CI/build status). Kept short so freshness is not sacrificed.
    /// Default: 15 seconds
    /// Environment variable: BITBUCKET_CACHE_TTL_SHORT_SECONDS
    /// </summary>
    public TimeSpan ShortCacheTtl { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Whether to enable graceful degradation (return stale cache on failure).
    /// Default: true
    /// </summary>
    public bool EnableGracefulDegradation { get; set; } = true;

    /// <summary>
    /// Maximum number of entries in the in-memory response cache.
    /// Default: 1000
    /// Environment variable: BITBUCKET_CACHE_SIZE_LIMIT
    /// </summary>
    public int CacheSizeLimit { get; set; } = 1000;

    /// <summary>
    /// Number of attempts to validate the Bitbucket connection at startup before failing fast.
    /// Transient failures (network, 5xx, rate limiting) are retried with exponential backoff;
    /// auth/permission failures fail immediately. If all attempts are exhausted the server
    /// still hard-fails (stops the host).
    /// Default: 3
    /// Environment variable: BITBUCKET_STARTUP_VALIDATION_ATTEMPTS
    /// </summary>
    public int StartupValidationAttempts { get; set; } = 3;

    /// <summary>
    /// Creates ResilienceSettings from environment variables with defaults.
    /// </summary>
    public static ResilienceSettings FromEnvironment()
    {
        var settings = new ResilienceSettings();

        if (int.TryParse(Environment.GetEnvironmentVariable("BITBUCKET_RETRY_COUNT"), out var retryCount))
        {
            settings.MaxRetryAttempts = Math.Clamp(retryCount, 0, 10);
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("BITBUCKET_CIRCUIT_TIMEOUT"), out var circuitTimeout))
        {
            settings.CircuitBreakerBreakDuration = TimeSpan.FromSeconds(Math.Clamp(circuitTimeout, 5, 300));
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("BITBUCKET_CACHE_TTL_SECONDS"), out var cacheTtl))
        {
            settings.DynamicCacheTtl = TimeSpan.FromSeconds(Math.Clamp(cacheTtl, 10, 600));
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("BITBUCKET_CACHE_TTL_STATIC_SECONDS"), out var staticTtl))
        {
            settings.StaticCacheTtl = TimeSpan.FromSeconds(Math.Clamp(staticTtl, 30, 3600));
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("BITBUCKET_CACHE_TTL_SHORT_SECONDS"), out var shortTtl))
        {
            settings.ShortCacheTtl = TimeSpan.FromSeconds(Math.Clamp(shortTtl, 5, 120));
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("BITBUCKET_CACHE_SIZE_LIMIT"), out var cacheSize))
        {
            settings.CacheSizeLimit = Math.Clamp(cacheSize, 100, 50_000);
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("BITBUCKET_STARTUP_VALIDATION_ATTEMPTS"), out var startupAttempts))
        {
            settings.StartupValidationAttempts = Math.Clamp(startupAttempts, 1, 10);
        }

        return settings;
    }
}