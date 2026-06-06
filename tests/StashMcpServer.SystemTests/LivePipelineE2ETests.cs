using Microsoft.Extensions.DependencyInjection;
using StashMcpServer.SystemTests.Infrastructure;
using StashMcpServer.Tools;

namespace StashMcpServer.SystemTests;

/// <summary>
/// End-to-end tests that resolve the real tool classes from the real service pipeline and invoke
/// them directly against a live, seeded Bitbucket container. No mocks, no MCP transport — just the
/// tools talking to a real server. Skipped unless <c>STASH_TEST_LICENSE</c> + Docker are present.
/// </summary>
[Collection(SystemTestCollection.Name)]
[Trait("Category", "SystemTest")]
public sealed class LivePipelineE2ETests(BitbucketServerFixture fixture)
{
    private async Task<IServiceProvider> PipelineAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason);
        return await fixture.GetPipelineAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task ListProjects_ReturnsSeededProject()
    {
        var tools = (await PipelineAsync()).GetRequiredService<ProjectTools>();

        var text = await tools.ListProjectsAsync();

        Assert.Contains(fixture.Seeded.ProjectName, text);
        Assert.Contains($"[{fixture.Seeded.ProjectKey}]", text);
    }

    [SkippableFact]
    public async Task ListRepositories_ReturnsSeededRepository()
    {
        var tools = (await PipelineAsync()).GetRequiredService<RepositoryTools>();

        var text = await tools.ListRepositoriesAsync(fixture.Seeded.ProjectKey);

        Assert.Contains(fixture.Seeded.RepositorySlug, text);
    }

    [SkippableFact]
    public async Task RepositoryOverview_IncludesDefaultBranchAndTag()
    {
        var tools = (await PipelineAsync()).GetRequiredService<RepositoryTools>();

        var text = await tools.GetRepositoryOverviewAsync(fixture.Seeded.ProjectKey, fixture.Seeded.RepositorySlug);

        Assert.Contains(fixture.Seeded.DefaultBranch, text);
        Assert.Contains(fixture.Seeded.TagName, text);
    }

    [SkippableFact]
    public async Task ListBranches_IncludesFeatureBranch()
    {
        var tools = (await PipelineAsync()).GetRequiredService<GitTools>();

        var text = await tools.ListBranchesAsync(fixture.Seeded.ProjectKey, fixture.Seeded.RepositorySlug);

        Assert.Contains(fixture.Seeded.FeatureBranch, text);
    }

    [SkippableFact]
    public async Task ListTags_IncludesSeededTag()
    {
        var tools = (await PipelineAsync()).GetRequiredService<GitTools>();

        var text = await tools.ListTagsAsync(fixture.Seeded.ProjectKey, fixture.Seeded.RepositorySlug);

        Assert.Contains(fixture.Seeded.TagName, text);
    }

    [SkippableFact]
    public async Task ListPullRequests_ReturnsSeededPullRequest()
    {
        var tools = (await PipelineAsync()).GetRequiredService<PullRequestTools>();

        var text = await tools.ListPullRequestsAsync(fixture.Seeded.ProjectKey, fixture.Seeded.RepositorySlug);

        Assert.Contains(fixture.Seeded.PullRequestTitle, text);
    }

    [SkippableFact]
    public async Task GetPullRequestDetails_ReturnsSeededPullRequest()
    {
        var tools = (await PipelineAsync()).GetRequiredService<PullRequestTools>();

        var text = await tools.GetPullRequestDetailsAsync(
            fixture.Seeded.ProjectKey, fixture.Seeded.RepositorySlug, (int)fixture.Seeded.PullRequestId);

        Assert.Contains(fixture.Seeded.PullRequestTitle, text);
    }

    [SkippableFact]
    public async Task GetPullRequestTasks_RunsAgainstLiveServer()
    {
        var tools = (await PipelineAsync()).GetRequiredService<PullRequestTools>();

        // On Bitbucket 9.x the legacy tasks endpoint is gone; this exercises the tool's real
        // graceful-degradation path end-to-end (it must not throw and must reference the PR).
        var text = await tools.GetPullRequestTasksAsync(
            fixture.Seeded.ProjectKey, fixture.Seeded.RepositorySlug, (int)fixture.Seeded.PullRequestId);

        Assert.Contains($"#{fixture.Seeded.PullRequestId}", text);
    }

    [SkippableFact]
    public async Task GetBuildStatus_ReturnsSeededSuccessfulBuild()
    {
        var tools = (await PipelineAsync()).GetRequiredService<BuildTools>();

        var text = await tools.GetBuildStatusAsync(fixture.Seeded.HeadCommitId);

        Assert.True(text.Contains("success", StringComparison.OrdinalIgnoreCase), text);
    }

    [SkippableFact]
    public async Task GetServerInfo_ReportsLiveServerVersion()
    {
        var tools = (await PipelineAsync()).GetRequiredService<DashboardTools>();

        var text = await tools.GetServerInfoAsync();

        // Proves the application-properties cache was populated from the live server. (We assert on
        // server info rather than get_current_user: that tool relies on the applinks /whoami servlet,
        // which doesn't resolve a token-authenticated user on a fresh instance, so it degrades to
        // "current user not identified" — real, but not a useful end-to-end assertion.)
        Assert.Contains("Bitbucket Server Information", text);
        Assert.DoesNotContain("Version: Unknown", text);
    }
}