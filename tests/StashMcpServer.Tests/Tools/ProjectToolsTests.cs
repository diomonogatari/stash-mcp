using Bitbucket.Net;
using Bitbucket.Net.Models.Core.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StashMcpServer.Services;
using StashMcpServer.Tools;

namespace StashMcpServer.Tests.Tools;

public class ProjectToolsTests
{
    private readonly IBitbucketCacheService _cacheService = Substitute.For<IBitbucketCacheService>();
    private readonly IResilientApiService _resilientApi = Substitute.For<IResilientApiService>();
    private readonly IBitbucketClient _client = Substitute.For<IBitbucketClient>();
    private readonly IServerSettings _serverSettings = Substitute.For<IServerSettings>();
    private readonly ProjectTools _sut;

    public ProjectToolsTests()
    {
        _sut = new ProjectTools(
            NullLogger<ProjectTools>.Instance,
            _cacheService,
            _resilientApi,
            _client,
            _serverSettings);
    }

    [Fact]
    public async Task ListProjectsAsync_WhenProjectsExist_ReturnsFormattedList()
    {
        var projects = new List<Project>
        {
            new() { Name = "Alpha Project", Key = "ALPHA" },
            new() { Name = "Beta Project", Key = "BETA" },
        };
        _cacheService.GetProjects().Returns(projects);

        var result = await _sut.ListProjectsAsync();

        Assert.Contains("Projects (2)", result);
        Assert.Contains("Alpha Project [ALPHA]", result);
        Assert.Contains("Beta Project [BETA]", result);
    }

    [Fact]
    public async Task ListProjectsAsync_WhenNoProjects_ReturnsNotFoundMessage()
    {
        _cacheService.GetProjects().Returns(new List<Project>());

        var result = await _sut.ListProjectsAsync();

        Assert.Equal("No projects found.", result);
    }

    [Fact]
    public async Task ListProjectsAsync_SortsProjectsByName()
    {
        var projects = new List<Project>
        {
            new() { Name = "Zebra", Key = "ZEB" },
            new() { Name = "Alpha", Key = "ALP" },
            new() { Name = "Middle", Key = "MID" },
        };
        _cacheService.GetProjects().Returns(projects);

        var result = await _sut.ListProjectsAsync();

        var alphaIndex = result.IndexOf("Alpha", StringComparison.Ordinal);
        var middleIndex = result.IndexOf("Middle", StringComparison.Ordinal);
        var zebraIndex = result.IndexOf("Zebra", StringComparison.Ordinal);

        Assert.True(alphaIndex < middleIndex);
        Assert.True(middleIndex < zebraIndex);
    }
}