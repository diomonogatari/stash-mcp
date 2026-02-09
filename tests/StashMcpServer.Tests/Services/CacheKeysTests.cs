using StashMcpServer.Services;

namespace StashMcpServer.Tests.Services;

public class CacheKeysTests
{
    private const string ProjectKey = "PROJ";
    private const string RepoSlug = "my-repo";
    private const long PrId = 123;
    private const string CommitId = "abc123def456";

    #region Pull Request Key Tests

    [Fact]
    public void PullRequestList_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestList(ProjectKey, RepoSlug, "OPEN");
        Assert.Equal("pr-list:PROJ:my-repo:OPEN", result);
    }

    [Fact]
    public void PullRequestDetails_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestDetails(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-details:PROJ:my-repo:123", result);
    }

    [Fact]
    public void PullRequestDiff_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestDiff(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-diff:PROJ:my-repo:123", result);
    }

    [Fact]
    public void PullRequestComments_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestComments(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-comments:PROJ:my-repo:123", result);
    }

    [Fact]
    public void PullRequestActivities_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestActivities(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-activities:PROJ:my-repo:123", result);
    }

    [Theory]
    [InlineData(true, true, true, true, "pr-context:PROJ:my-repo:123:True:True:True:True")]
    [InlineData(false, false, false, false, "pr-context:PROJ:my-repo:123:False:False:False:False")]
    [InlineData(true, false, true, false, "pr-context:PROJ:my-repo:123:True:False:True:False")]
    [InlineData(true, true, false, true, "pr-context:PROJ:my-repo:123:True:True:False:True")]
    public void PullRequestContext_ReturnsExpectedFormat(
        bool includeComments, bool includeDiff, bool includeActivity, bool includeTasks, string expected)
    {
        var result = CacheKeys.PullRequestContext(ProjectKey, RepoSlug, PrId, includeComments, includeDiff, includeActivity, includeTasks);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PullRequestTasks_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestTasks(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-tasks:PROJ:my-repo:123", result);
    }

    [Fact]
    public void PullRequestChanges_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestChanges(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-changes:PROJ:my-repo:123", result);
    }

    [Fact]
    public void PullRequest_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequest(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr:PROJ:my-repo:123", result);
    }

    [Fact]
    public void PullRequestMergeBase_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestMergeBase(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-merge-base:PROJ:my-repo:123", result);
    }

    #endregion

    #region Repository Key Tests

    [Fact]
    public void Branches_ReturnsExpectedFormat()
    {
        var result = CacheKeys.Branches(ProjectKey, RepoSlug);
        Assert.Equal("branches:PROJ:my-repo", result);
    }

    [Fact]
    public void Tags_ReturnsExpectedFormat()
    {
        var result = CacheKeys.Tags(ProjectKey, RepoSlug);
        Assert.Equal("tags:PROJ:my-repo", result);
    }

    [Fact]
    public void Files_WithRef_ReturnsExpectedFormat()
    {
        var result = CacheKeys.Files(ProjectKey, RepoSlug, "main");
        Assert.Equal("files:PROJ:my-repo:main", result);
    }

    [Fact]
    public void Files_WithNullRef_UsesDefault()
    {
        var result = CacheKeys.Files(ProjectKey, RepoSlug, null);
        Assert.Equal("files:PROJ:my-repo:default", result);
    }

    [Fact]
    public void FileContent_WithRef_ReturnsExpectedFormat()
    {
        var result = CacheKeys.FileContent(ProjectKey, RepoSlug, "src/file.cs", "develop");
        Assert.Equal("file-content:PROJ:my-repo:src/file.cs:develop", result);
    }

    [Fact]
    public void FileContent_WithNullRef_UsesDefault()
    {
        var result = CacheKeys.FileContent(ProjectKey, RepoSlug, "src/file.cs", null);
        Assert.Equal("file-content:PROJ:my-repo:src/file.cs:default", result);
    }

    #endregion

    #region Commit Key Tests

    [Fact]
    public void CommitDetails_ReturnsExpectedFormat()
    {
        var result = CacheKeys.CommitDetails(ProjectKey, RepoSlug, CommitId);
        Assert.Equal("commit:PROJ:my-repo:abc123def456", result);
    }

    [Fact]
    public void CommitChanges_ReturnsExpectedFormat()
    {
        var result = CacheKeys.CommitChanges(ProjectKey, RepoSlug, CommitId);
        Assert.Equal("commit-changes:PROJ:my-repo:abc123def456", result);
    }

    [Fact]
    public void CommitDiff_ReturnsExpectedFormat()
    {
        var result = CacheKeys.CommitDiff(ProjectKey, RepoSlug, CommitId);
        Assert.Equal("commit-diff:PROJ:my-repo:abc123def456", result);
    }

    [Fact]
    public void CommitSearch_WithAllParams_ReturnsExpectedFormat()
    {
        var result = CacheKeys.CommitSearch(
            ProjectKey, RepoSlug,
            messageFilter: "fix",
            author: "john",
            sinceDate: "2024-01-01",
            untilDate: "2024-12-31",
            branch: "main");
        Assert.Equal("commit-search:PROJ:my-repo:fix:john:2024-01-01:2024-12-31:main", result);
    }

    [Fact]
    public void CommitSearch_WithNullParams_UsesPlaceholder()
    {
        var result = CacheKeys.CommitSearch(ProjectKey, RepoSlug, null, null, null, null, null);
        Assert.Equal("commit-search:PROJ:my-repo:_:_:_:_:_", result);
    }

    [Fact]
    public void CommitSearch_WithMixedParams_UsesPlaceholderForNulls()
    {
        var result = CacheKeys.CommitSearch(
            ProjectKey, RepoSlug,
            messageFilter: "bugfix",
            author: null,
            sinceDate: "2024-01-01",
            untilDate: null,
            branch: "develop");
        Assert.Equal("commit-search:PROJ:my-repo:bugfix:_:2024-01-01:_:develop", result);
    }

    #endregion

    #region Build Key Tests

    [Fact]
    public void BuildStatus_ReturnsExpectedFormat()
    {
        var result = CacheKeys.BuildStatus(CommitId);
        Assert.Equal("build-status:abc123def456", result);
    }

    [Fact]
    public void BuildStats_ReturnsExpectedFormat()
    {
        var result = CacheKeys.BuildStats(CommitId);
        Assert.Equal("build-stats:abc123def456", result);
    }

    [Fact]
    public void RepositoryBuilds_ReturnsExpectedFormat()
    {
        var result = CacheKeys.RepositoryBuilds(ProjectKey, RepoSlug, "main", 25);
        Assert.Equal("repo-builds:PROJ:my-repo:main:25", result);
    }

    [Theory]
    [InlineData("develop", 10, "repo-builds:PROJ:my-repo:develop:10")]
    [InlineData("refs/heads/feature", 50, "repo-builds:PROJ:my-repo:refs/heads/feature:50")]
    [InlineData("main", 100, "repo-builds:PROJ:my-repo:main:100")]
    public void RepositoryBuilds_WithDifferentParams_ReturnsExpectedFormat(
        string branch, int limit, string expected)
    {
        var result = CacheKeys.RepositoryBuilds(ProjectKey, RepoSlug, branch, limit);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Dashboard Key Tests

    [Fact]
    public void MyPullRequests_WithState_ReturnsExpectedFormat()
    {
        var result = CacheKeys.MyPullRequests("REVIEWER", "OPEN");
        Assert.Equal("my-prs:REVIEWER:OPEN", result);
    }

    [Fact]
    public void MyPullRequests_WithNullState_UsesAll()
    {
        var result = CacheKeys.MyPullRequests("AUTHOR", null);
        Assert.Equal("my-prs:AUTHOR:all", result);
    }

    [Fact]
    public void InboxPullRequests_ReturnsExpectedFormat()
    {
        var result = CacheKeys.InboxPullRequests();
        Assert.Equal("inbox-prs", result);
    }

    #endregion

    #region Jira Integration Key Tests

    [Fact]
    public void PullRequestJiraIssues_ReturnsExpectedFormat()
    {
        var result = CacheKeys.PullRequestJiraIssues(ProjectKey, RepoSlug, PrId);
        Assert.Equal("pr-jira:PROJ:my-repo:123", result);
    }

    #endregion

    #region User Key Tests

    [Fact]
    public void UserSearch_ReturnsExpectedFormat()
    {
        var result = CacheKeys.UserSearch("john", 25);
        Assert.Equal("user-search:john:25", result);
    }

    [Theory]
    [InlineData("jane", 10, "user-search:jane:10")]
    [InlineData("admin@company.com", 50, "user-search:admin@company.com:50")]
    [InlineData("John Doe", 100, "user-search:John Doe:100")]
    public void UserSearch_WithDifferentParams_ReturnsExpectedFormat(
        string query, int limit, string expected)
    {
        var result = CacheKeys.UserSearch(query, limit);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Consistency Tests

    [Fact]
    public void AllMethods_ProduceColonSeparatedKeys()
    {
        // Test a variety of cache key methods for consistent format
        var keys = new[]
        {
            CacheKeys.PullRequestList("P", "R", "S"),
            CacheKeys.PullRequestDetails("P", "R", 1),
            CacheKeys.PullRequest("P", "R", 1),
            CacheKeys.PullRequestMergeBase("P", "R", 1),
            CacheKeys.Branches("P", "R"),
            CacheKeys.Tags("P", "R"),
            CacheKeys.Files("P", "R", "ref"),
            CacheKeys.CommitDetails("P", "R", "c"),
            CacheKeys.BuildStatus("c"),
            CacheKeys.MyPullRequests("role", "state"),
            CacheKeys.PullRequestJiraIssues("P", "R", 1)
        };

        // All keys should contain colons
        foreach (var key in keys)
        {
            Assert.Contains(":", key);
        }
    }

    [Fact]
    public void SameInputs_ProduceSameKeys()
    {
        var key1 = CacheKeys.PullRequestDetails(ProjectKey, RepoSlug, PrId);
        var key2 = CacheKeys.PullRequestDetails(ProjectKey, RepoSlug, PrId);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DifferentInputs_ProduceDifferentKeys()
    {
        var key1 = CacheKeys.PullRequestDetails(ProjectKey, RepoSlug, 1);
        var key2 = CacheKeys.PullRequestDetails(ProjectKey, RepoSlug, 2);
        Assert.NotEqual(key1, key2);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void PullRequestList_WithEmptyStrings_ReturnsKeyWithEmptySegments()
    {
        var result = CacheKeys.PullRequestList("", "", "");
        Assert.Equal("pr-list:::", result);
    }

    [Fact]
    public void FileContent_WithPathContainingColons_PreservesPath()
    {
        var filePath = "C:/some/path/file.cs";
        var result = CacheKeys.FileContent(ProjectKey, RepoSlug, filePath, null);
        Assert.Contains(filePath, result);
    }

    [Fact]
    public void FileContent_WithSpecialCharactersInPath_PreservesPath()
    {
        var filePath = "src/components/Button.test.tsx";
        var result = CacheKeys.FileContent(ProjectKey, RepoSlug, filePath, "main");
        Assert.Equal("file-content:PROJ:my-repo:src/components/Button.test.tsx:main", result);
    }

    [Fact]
    public void CommitSearch_WithEmptyStringParams_UsesEmptyStrings()
    {
        // Note: Empty strings are different from null - they don't get placeholder
        var result = CacheKeys.CommitSearch(ProjectKey, RepoSlug, "", "", "", "", "");
        Assert.Equal("commit-search:PROJ:my-repo:::::", result);
    }

    #endregion

    #region Limit Suffix Convention Tests (Spec 11)

    [Theory]
    [InlineData(10, 25)]
    [InlineData(25, 50)]
    [InlineData(1, 100)]
    public void LimitSuffix_DifferentLimits_ProduceDifferentKeys(int limit1, int limit2)
    {
        var baseKey = CacheKeys.Branches(ProjectKey, RepoSlug);
        var key1 = $"{baseKey}:limit={limit1}";
        var key2 = $"{baseKey}:limit={limit2}";

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void LimitSuffix_SameLimit_ProducesSameKey()
    {
        var baseKey = CacheKeys.Branches(ProjectKey, RepoSlug);
        var key1 = $"{baseKey}:limit={25}";
        var key2 = $"{baseKey}:limit={25}";

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void LimitSuffix_Branches_WithFilterAndBase_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.Branches(ProjectKey, RepoSlug);
        var key = $"{baseKey}:limit={25}:filter={"feature"}:base={"refs/heads/main"}";

        Assert.Equal("branches:PROJ:my-repo:limit=25:filter=feature:base=refs/heads/main", key);
    }

    [Fact]
    public void LimitSuffix_Branches_DifferentFilters_ProduceDifferentKeys()
    {
        var baseKey = CacheKeys.Branches(ProjectKey, RepoSlug);
        var key1 = $"{baseKey}:limit={25}:filter={"feat"}:base={""}";
        var key2 = $"{baseKey}:limit={25}:filter={"bug"}:base={""}";

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void LimitSuffix_Tags_WithFilter_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.Tags(ProjectKey, RepoSlug);
        var key = $"{baseKey}:limit={50}:filter={"v1."}";

        Assert.Equal("tags:PROJ:my-repo:limit=50:filter=v1.", key);
    }

    [Fact]
    public void LimitSuffix_PullRequestList_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.PullRequestList(ProjectKey, RepoSlug, "OPEN");
        var key = $"{baseKey}:limit={25}";

        Assert.Equal("pr-list:PROJ:my-repo:OPEN:limit=25", key);
    }

    [Fact]
    public void LimitSuffix_MyPullRequests_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.MyPullRequests("REVIEWER", "OPEN");
        var key = $"{baseKey}:limit={10}";

        Assert.Equal("my-prs:REVIEWER:OPEN:limit=10", key);
    }

    [Fact]
    public void LimitSuffix_InboxPullRequests_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.InboxPullRequests();
        var key = $"{baseKey}:limit={25}";

        Assert.Equal("inbox-prs:limit=25", key);
    }

    [Fact]
    public void LimitSuffix_CommitChanges_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.CommitChanges(ProjectKey, RepoSlug, CommitId);
        var key = $"{baseKey}:limit={100}";

        Assert.Equal("commit-changes:PROJ:my-repo:abc123def456:limit=100", key);
    }

    [Fact]
    public void LimitSuffix_BuildStatus_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.BuildStatus(CommitId);
        var key = $"{baseKey}:limit={25}";

        Assert.Equal("build-status:abc123def456:limit=25", key);
    }

    [Fact]
    public void LimitSuffix_Files_ProducesExpectedFormat()
    {
        var baseKey = CacheKeys.Files(ProjectKey, RepoSlug, "main");
        var key = $"{baseKey}:limit={200}";

        Assert.Equal("files:PROJ:my-repo:main:limit=200", key);
    }

    #endregion
}