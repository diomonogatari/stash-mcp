using StashMcpServer.Configuration;

namespace StashMcpServer.Tests.Configuration;

public class ServerSettingsTests
{
    #region FromEnvironment Tests

    [Fact]
    public void FromEnvironment_WhenNoEnvVar_ReturnsFalseReadOnlyMode()
    {
        var originalValue = Environment.GetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE");
        try
        {
            Environment.SetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE", null);
            var settings = ServerSettings.FromEnvironment();
            Assert.False(settings.ReadOnlyMode);
        }
        finally
        {
            // Restore original value
            Environment.SetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE", originalValue);
        }
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("1")]
    public void FromEnvironment_WhenEnvVarTrue_ReturnsTrueReadOnlyMode(string envValue)
    {
        var originalValue = Environment.GetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE");
        try
        {
            Environment.SetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE", envValue);
            var settings = ServerSettings.FromEnvironment();
            Assert.True(settings.ReadOnlyMode);
        }
        finally
        {
            // Restore original value
            Environment.SetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE", originalValue);
        }
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("")]
    [InlineData("random")]
    public void FromEnvironment_WhenEnvVarNotTrue_ReturnsFalseReadOnlyMode(string envValue)
    {
        var originalValue = Environment.GetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE");
        try
        {
            Environment.SetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE", envValue);
            var settings = ServerSettings.FromEnvironment();
            Assert.False(settings.ReadOnlyMode);
        }
        finally
        {
            // Restore original value
            Environment.SetEnvironmentVariable("BITBUCKET_READ_ONLY_MODE", originalValue);
        }
    }

    #endregion

    #region ReadOnlyErrorMessage Tests

    [Fact]
    public void ReadOnlyErrorMessage_ContainsExpectedContent()
    {
        Assert.Contains("Read-Only Mode", ServerSettings.ReadOnlyErrorMessage);
        Assert.Contains("BITBUCKET_READ_ONLY_MODE", ServerSettings.ReadOnlyErrorMessage);
        Assert.Contains("Write operations are disabled", ServerSettings.ReadOnlyErrorMessage);
    }

    [Fact]
    public void ReadOnlyErrorMessage_StartsWithErrorEmoji()
    {
        Assert.StartsWith("❌", ServerSettings.ReadOnlyErrorMessage);
    }

    #endregion
}