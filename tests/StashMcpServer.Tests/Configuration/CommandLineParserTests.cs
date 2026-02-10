using Microsoft.Extensions.Logging;
using Serilog.Events;
using StashMcpServer.Configuration;

namespace StashMcpServer.Tests.Configuration;

public class CommandLineParserTests
{
    [Fact]
    public void ParseArguments_NoArgs_ReturnsDefaults()
    {
        var (stashUrl, pat, logLevel) = CommandLineParser.ParseArguments(
            [], defaultStashUrl: null, defaultPat: null);

        Assert.Null(stashUrl);
        Assert.Null(pat);
        Assert.Equal(LogEventLevel.Information, logLevel);
    }

    [Fact]
    public void ParseArguments_NoArgs_ReturnsEnvironmentDefaults()
    {
        var (stashUrl, pat, logLevel) = CommandLineParser.ParseArguments(
            [], defaultStashUrl: "https://stash.example.com/", defaultPat: "secret");

        Assert.Equal("https://stash.example.com/", stashUrl);
        Assert.Equal("secret", pat);
        Assert.Equal(LogEventLevel.Information, logLevel);
    }

    [Fact]
    public void ParseArguments_StashUrlFlag_OverridesDefault()
    {
        var (stashUrl, _, _) = CommandLineParser.ParseArguments(
            ["--stash-url", "https://custom.example.com/"],
            defaultStashUrl: "https://default.example.com/",
            defaultPat: null);

        Assert.Equal("https://custom.example.com/", stashUrl);
    }

    [Fact]
    public void ParseArguments_PatFlag_OverridesDefault()
    {
        var (_, pat, _) = CommandLineParser.ParseArguments(
            ["--pat", "cli-token"],
            defaultStashUrl: null,
            defaultPat: "env-token");

        Assert.Equal("cli-token", pat);
    }

    [Theory]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("Verbose", LogEventLevel.Verbose)]
    [InlineData("Fatal", LogEventLevel.Fatal)]
    public void ParseArguments_LogLevelFlag_ParsesCaseInsensitive(string levelValue, LogEventLevel expected)
    {
        var (_, _, logLevel) = CommandLineParser.ParseArguments(
            ["--log-level", levelValue],
            defaultStashUrl: null,
            defaultPat: null);

        Assert.Equal(expected, logLevel);
    }

    [Fact]
    public void ParseArguments_InvalidLogLevel_DefaultsToInformation()
    {
        var (_, _, logLevel) = CommandLineParser.ParseArguments(
            ["--log-level", "NotALevel"],
            defaultStashUrl: null,
            defaultPat: null);

        Assert.Equal(LogEventLevel.Information, logLevel);
    }

    [Fact]
    public void ParseArguments_AllFlags_ParsesCorrectly()
    {
        var (stashUrl, pat, logLevel) = CommandLineParser.ParseArguments(
            ["--stash-url", "https://stash.example.com/", "--pat", "my-token", "--log-level", "Debug"],
            defaultStashUrl: null,
            defaultPat: null);

        Assert.Equal("https://stash.example.com/", stashUrl);
        Assert.Equal("my-token", pat);
        Assert.Equal(LogEventLevel.Debug, logLevel);
    }

    [Fact]
    public void ParseArguments_UnknownFlags_AreIgnored()
    {
        var (stashUrl, pat, logLevel) = CommandLineParser.ParseArguments(
            ["--unknown", "value", "--stash-url", "https://stash.example.com/"],
            defaultStashUrl: null,
            defaultPat: null);

        Assert.Equal("https://stash.example.com/", stashUrl);
        Assert.Null(pat);
        Assert.Equal(LogEventLevel.Information, logLevel);
    }

    [Fact]
    public void ParseArguments_FlagWithoutValue_IsIgnored()
    {
        var (stashUrl, _, _) = CommandLineParser.ParseArguments(
            ["--stash-url"],
            defaultStashUrl: "https://default.example.com/",
            defaultPat: null);

        Assert.Equal("https://default.example.com/", stashUrl);
    }

    [Theory]
    [InlineData(LogEventLevel.Verbose, LogLevel.Trace)]
    [InlineData(LogEventLevel.Debug, LogLevel.Debug)]
    [InlineData(LogEventLevel.Information, LogLevel.Information)]
    [InlineData(LogEventLevel.Warning, LogLevel.Warning)]
    [InlineData(LogEventLevel.Error, LogLevel.Error)]
    [InlineData(LogEventLevel.Fatal, LogLevel.Critical)]
    public void MapToMicrosoftLogLevel_MapsCorrectly(LogEventLevel serilog, LogLevel expected)
    {
        var result = CommandLineParser.MapToMicrosoftLogLevel(serilog);

        Assert.Equal(expected, result);
    }
