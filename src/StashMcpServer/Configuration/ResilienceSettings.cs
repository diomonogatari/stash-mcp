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
    /// TTL for cached dynamic data (PR lists, comments).
    /// Default: 60 seconds
    /// Environment variable: BITBUCKET_CACHE_TTL_SECONDS
    /// </summary>
    public TimeSpan DynamicCacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether to enable graceful degradation (return stale cache on failure).
    /// Default: true
    /// </summary>
    public bool EnableGracefulDegradation { get; set; } = true;

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

        return settings;
    }
}