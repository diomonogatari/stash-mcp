using Bitbucket.Net;
using Bitbucket.Net.Models.Core.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StashMcpServer.Services;
using StashMcpServer.Tools;

namespace StashMcpServer.Tests.Tools;

public class ToolBaseTests
{
    private readonly IBitbucketCacheService _cacheService = Substitute.For<IBitbucketCacheService>();
    private readonly IResilientApiService _resilientApi = Substitute.For<IResilientApiService>();
    private readonly BitbucketClient _client = new("http://localhost", () => "dummy");
    private readonly IServerSettings _serverSettings = Substitute.For<IServerSettings>();
    private readonly TestToolBase _sut;

    public ToolBaseTests()
    {
        _sut = new TestToolBase(
            NullLogger<TestToolBase>.Instance,
            _cacheService,
            _resilientApi,
            _client,
            _serverSettings);
    }

    [Fact]
    public void CheckReadOnlyMode_WhenReadOnly_ReturnsErrorMessage()
    {
        _serverSettings.ReadOnlyMode.Returns(true);

        var result = _sut.InvokeCheckReadOnlyMode();

        Assert.NotNull(result);
        Assert.Contains("Read-Only Mode", result);
    }

    [Fact]
    public void CheckReadOnlyMode_WhenWriteAllowed_ReturnsNull()
    {
        _serverSettings.ReadOnlyMode.Returns(false);

        var result = _sut.InvokeCheckReadOnlyMode();

        Assert.Null(result);
    }

    [Fact]
    public void NormalizeProjectKey_TrimsWhitespace()
    {
        _cacheService.FindProject(Arg.Any<string>()).Returns((Project?)null);

        var result = _sut.InvokeNormalizeProjectKey("  PROJ  ");

        Assert.Equal("PROJ", result);
    }

    [Fact]
    public void NormalizeProjectKey_UsesCanonicalKeyFromCache()
    {
        _cacheService.FindProject("proj").Returns(new Project { Key = "PROJ" });

        var result = _sut.InvokeNormalizeProjectKey("proj");

        Assert.Equal("PROJ", result);
    }

    [Fact]
    public void NormalizeProjectKey_NoCacheHit_ReturnsTrimmedInput()
    {
        _cacheService.FindProject(Arg.Any<string>()).Returns((Project?)null);

        var result = _sut.InvokeNormalizeProjectKey("custom");

        Assert.Equal("custom", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeProjectKey_EmptyOrNull_ThrowsArgumentException(string? key)
    {
        Assert.Throws<ArgumentException>(() => _sut.InvokeNormalizeProjectKey(key!));
    }

    [Fact]
    public void NormalizeRepositorySlug_TrimsWhitespace()
    {
        _cacheService.FindRepository(Arg.Any<string>(), Arg.Any<string>()).Returns((Repository?)null);

        var result = _sut.InvokeNormalizeRepositorySlug("PROJ", "  my-repo  ");

        Assert.Equal("my-repo", result);
    }

    [Fact]
    public void NormalizeRepositorySlug_UsesCanonicalSlugFromCache()
    {
        _cacheService.FindRepository("PROJ", "myrepo")
            .Returns(new Repository { Slug = "my-repo" });

        var result = _sut.InvokeNormalizeRepositorySlug("PROJ", "myrepo");

        Assert.Equal("my-repo", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeRepositorySlug_EmptyOrNull_ThrowsArgumentException(string? slug)
    {
        Assert.Throws<ArgumentException>(() => _sut.InvokeNormalizeRepositorySlug("PROJ", slug!));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestToolBase(null!, _cacheService, _resilientApi, _client, _serverSettings));
    }

    [Fact]
    public void Constructor_NullCacheService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestToolBase(NullLogger<TestToolBase>.Instance, null!, _resilientApi, _client, _serverSettings));
    }

    [Fact]
    public void Constructor_NullResilientApi_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestToolBase(NullLogger<TestToolBase>.Instance, _cacheService, null!, _client, _serverSettings));
    }

    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestToolBase(NullLogger<TestToolBase>.Instance, _cacheService, _resilientApi, null!, _serverSettings));
    }

    [Fact]
    public void Constructor_NullServerSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TestToolBase(NullLogger<TestToolBase>.Instance, _cacheService, _resilientApi, _client, null!));
    }

    /// <summary>
    /// Concrete subclass of ToolBase to expose protected methods for testing.
    /// </summary>
    private sealed class TestToolBase(
        ILogger<TestToolBase> logger,
        IBitbucketCacheService cacheService,
        IResilientApiService resilientApi,
        BitbucketClient client,
        IServerSettings serverSettings)
        : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
    {
        public string? InvokeCheckReadOnlyMode() => CheckReadOnlyMode();
        public string InvokeNormalizeProjectKey(string projectKey) => NormalizeProjectKey(projectKey);
        public string InvokeNormalizeRepositorySlug(string projectKey, string slug) => NormalizeRepositorySlug(projectKey, slug);
    }
}
