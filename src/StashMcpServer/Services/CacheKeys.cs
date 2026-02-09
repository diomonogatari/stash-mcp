namespace StashMcpServer.Services;

/// <summary>
/// Provides consistent cache key generation for Bitbucket API operations.
/// Convention: {operation}:{project}:{repo}[:{resource-id}]
/// </summary>
public static class CacheKeys
{
    // Pull Request operations
    public static string PullRequestList(string projectKey, string repoSlug, string state) =>
        $"pr-list:{projectKey}:{repoSlug}:{state}";

    public static string PullRequestDetails(string projectKey, string repoSlug, long prId) =>
        $"pr-details:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequestDiff(string projectKey, string repoSlug, long prId) =>
        $"pr-diff:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequestComments(string projectKey, string repoSlug, long prId) =>
        $"pr-comments:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequestActivities(string projectKey, string repoSlug, long prId) =>
        $"pr-activities:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequestContext(string projectKey, string repoSlug, long prId, bool includeComments, bool includeDiff, bool includeActivity, bool includeTasks) =>
        $"pr-context:{projectKey}:{repoSlug}:{prId}:{includeComments}:{includeDiff}:{includeActivity}:{includeTasks}";

    public static string PullRequestTasks(string projectKey, string repoSlug, long prId) =>
        $"pr-tasks:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequestBlockerComments(string projectKey, string repoSlug, long prId) =>
        $"pr-blocker-comments:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequestChanges(string projectKey, string repoSlug, long prId) =>
        $"pr-changes:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequest(string projectKey, string repoSlug, long prId) =>
        $"pr:{projectKey}:{repoSlug}:{prId}";

    public static string PullRequestMergeBase(string projectKey, string repoSlug, long prId) =>
        $"pr-merge-base:{projectKey}:{repoSlug}:{prId}";

    // Repository operations
    public static string Branches(string projectKey, string repoSlug) =>
        $"branches:{projectKey}:{repoSlug}";

    public static string Tags(string projectKey, string repoSlug) =>
        $"tags:{projectKey}:{repoSlug}";

    public static string Files(string projectKey, string repoSlug, string? @ref) =>
        $"files:{projectKey}:{repoSlug}:{@ref ?? "default"}";

    public static string FileContent(string projectKey, string repoSlug, string filePath, string? @ref) =>
        $"file-content:{projectKey}:{repoSlug}:{filePath}:{@ref ?? "default"}";

    // Commit operations
    public static string CommitDetails(string projectKey, string repoSlug, string commitId) =>
        $"commit:{projectKey}:{repoSlug}:{commitId}";

    public static string CommitChanges(string projectKey, string repoSlug, string commitId) =>
        $"commit-changes:{projectKey}:{repoSlug}:{commitId}";

    public static string CommitDiff(string projectKey, string repoSlug, string commitId) =>
        $"commit-diff:{projectKey}:{repoSlug}:{commitId}";

    public static string CommitSearch(
        string projectKey,
        string repoSlug,
        string? messageFilter,
        string? author,
        string? sinceDate,
        string? untilDate,
        string? branch) =>
        $"commit-search:{projectKey}:{repoSlug}:{messageFilter ?? "_"}:{author ?? "_"}:{sinceDate ?? "_"}:{untilDate ?? "_"}:{branch ?? "_"}";

    public static string CommitList(string projectKey, string repoSlug, string? @ref, string? path) =>
        $"commit-list:{projectKey}:{repoSlug}:{@ref ?? "default"}:{path ?? "_"}";

    // Build operations
    // Note: Build status is global by commit hash and does not require project/repository context
    public static string BuildStatus(string commitId) =>
        $"build-status:{commitId}";

    public static string BuildStats(string commitId) =>
        $"build-stats:{commitId}";

    public static string RepositoryBuilds(string projectKey, string repoSlug, string branch, int limit) =>
        $"repo-builds:{projectKey}:{repoSlug}:{branch}:{limit}";

    // Dashboard operations
    public static string MyPullRequests(string role, string? state) =>
        $"my-prs:{role}:{state ?? "all"}";

    public static string InboxPullRequests() =>
        "inbox-prs";

    // Jira integration
    public static string PullRequestJiraIssues(string projectKey, string repoSlug, long prId) =>
        $"pr-jira:{projectKey}:{repoSlug}:{prId}";

    // User operations
    public static string UserSearch(string query, int limit) =>
        $"user-search:{query}:{limit}";
}