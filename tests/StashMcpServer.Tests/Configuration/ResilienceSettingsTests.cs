using StashMcpServer.Configuration;

namespace StashMcpServer.Tests.Configuration;

public class ResilienceSettingsTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnvVars = new();

    public ResilienceSettingsTests()
    {
        // Save original env vars before tests
        SaveEnvVar("BITBUCKET_RETRY_COUNT");
        SaveEnvVar("BITBUCKET_CIRCUIT_TIMEOUT");
        SaveEnvVar("BITBUCKET_CACHE_TTL_SECONDS");
    }

    public void Dispose()
    {
        // Restore original env vars after tests
        foreach (var kvp in _originalEnvVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }
    }

    private void SaveEnvVar(string name)
    {
        _originalEnvVars[name] = Environment.GetEnvironmentVariable(name);
    }

    private void ClearEnvVars()
    {
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", null);
        Environment.SetEnvironmentVariable("BITBUCKET_CIRCUIT_TIMEOUT", null);
        Environment.SetEnvironmentVariable("BITBUCKET_CACHE_TTL_SECONDS", null);
    }

    #region Default Values Tests

    [Fact]
    public void FromEnvironment_WhenNoEnvVarsSet_ReturnsDefaultValues()
    {
        ClearEnvVars();
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(3, settings.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(500), settings.RetryBaseDelay);
        Assert.Equal(0.5, settings.CircuitBreakerFailureRatio);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.CircuitBreakerSamplingDuration);
        Assert.Equal(10, settings.CircuitBreakerMinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.CircuitBreakerBreakDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.DynamicCacheTtl);
        Assert.True(settings.EnableGracefulDegradation);
    }

    #endregion

    #region Custom Values Tests

    [Fact]
    public void FromEnvironment_WhenRetryCountSet_ParsesCorrectly()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", "5");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(5, settings.MaxRetryAttempts);
    }

    [Fact]
    public void FromEnvironment_WhenCircuitTimeoutSet_ParsesCorrectly()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_CIRCUIT_TIMEOUT", "60");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(TimeSpan.FromSeconds(60), settings.CircuitBreakerBreakDuration);
    }

    [Fact]
    public void FromEnvironment_WhenCacheTtlSet_ParsesCorrectly()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_CACHE_TTL_SECONDS", "120");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(TimeSpan.FromSeconds(120), settings.DynamicCacheTtl);
    }

    [Fact]
    public void FromEnvironment_WhenAllEnvVarsSet_ParsesAllCorrectly()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", "7");
        Environment.SetEnvironmentVariable("BITBUCKET_CIRCUIT_TIMEOUT", "90");
        Environment.SetEnvironmentVariable("BITBUCKET_CACHE_TTL_SECONDS", "300");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(7, settings.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(90), settings.CircuitBreakerBreakDuration);
        Assert.Equal(TimeSpan.FromSeconds(300), settings.DynamicCacheTtl);
    }

    #endregion

    #region Boundary/Edge Cases Tests

    [Fact]
    public void FromEnvironment_WhenRetryCountExceedsMax_ClampsToMax()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", "100");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(10, settings.MaxRetryAttempts);
    }

    [Fact]
    public void FromEnvironment_WhenRetryCountBelowMin_ClampsToMin()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", "-5");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(0, settings.MaxRetryAttempts);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("10", 10)]
    [InlineData("5", 5)]
    public void FromEnvironment_RetryCountBoundaryValues_ClampsCorrectly(string envValue, int expected)
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", envValue);
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(expected, settings.MaxRetryAttempts);
    }

    [Fact]
    public void FromEnvironment_WhenCircuitTimeoutBelowMin_ClampsToMin()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_CIRCUIT_TIMEOUT", "1");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(TimeSpan.FromSeconds(5), settings.CircuitBreakerBreakDuration);
    }

    [Fact]
    public void FromEnvironment_WhenCircuitTimeoutExceedsMax_ClampsToMax()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_CIRCUIT_TIMEOUT", "1000");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(TimeSpan.FromSeconds(300), settings.CircuitBreakerBreakDuration);
    }

    [Fact]
    public void FromEnvironment_WhenCacheTtlBelowMin_ClampsToMin()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_CACHE_TTL_SECONDS", "1");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(TimeSpan.FromSeconds(10), settings.DynamicCacheTtl);
    }

    [Fact]
    public void FromEnvironment_WhenCacheTtlExceedsMax_ClampsToMax()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_CACHE_TTL_SECONDS", "1000");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(TimeSpan.FromSeconds(600), settings.DynamicCacheTtl);
    }

    [Fact]
    public void FromEnvironment_WhenEnvVarIsInvalidNumber_KeepsDefault()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", "not-a-number");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(3, settings.MaxRetryAttempts);
    }

    [Fact]
    public void FromEnvironment_WhenEnvVarIsEmpty_KeepsDefault()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", "");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(3, settings.MaxRetryAttempts);
    }

    [Fact]
    public void FromEnvironment_WhenEnvVarHasWhitespace_KeepsDefault()
    {
        ClearEnvVars();
        Environment.SetEnvironmentVariable("BITBUCKET_RETRY_COUNT", "   ");
        var settings = ResilienceSettings.FromEnvironment();
        Assert.Equal(3, settings.MaxRetryAttempts);
    }

    #endregion
}