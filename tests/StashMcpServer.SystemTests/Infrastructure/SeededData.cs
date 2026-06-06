namespace StashMcpServer.SystemTests.Infrastructure;

/// <summary>
/// The fixtures the <see cref="BitbucketSeeder"/> created on the live instance, plus the HTTP
/// access token the MCP server authenticates with. Tests assert against these known values.
/// </summary>
public sealed record SeededData
{
    /// <summary>Bearer token (a Bitbucket HTTP access token) the MCP server uses.</summary>
    public required string AccessToken { get; init; }

    public required string ProjectKey { get; init; }

    public required string ProjectName { get; init; }

    public required string RepositorySlug { get; init; }

    public required string DefaultBranch { get; init; }

    public required string FeatureBranch { get; init; }

    public required long PullRequestId { get; init; }

    public required string PullRequestTitle { get; init; }

    /// <summary>HEAD commit of the default branch — the one a build status was attached to.</summary>
    public required string HeadCommitId { get; init; }

    public required string TagName { get; init; }
}