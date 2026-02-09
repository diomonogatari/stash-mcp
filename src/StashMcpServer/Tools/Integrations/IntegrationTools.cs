using Bitbucket.Net;
using ModelContextProtocol.Server;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class IntegrationTools(
    ILogger<IntegrationTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    BitbucketClient client,
    IServerSettings serverSettings)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    [McpServerTool(Name = "get_pull_request_jira_issues"), Description("Get Jira issues linked to a pull request via commit messages and branch names.")]
    public async Task<string> GetPullRequestJiraIssuesAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestJiraIssuesAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"pr-jira-issues:{normalizedProjectKey}:{normalizedSlug}:{pullRequestId}";
        var issues = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetJiraIssuesAsync(normalizedProjectKey, normalizedSlug, pullRequestId)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var issueList = issues.ToList();

        if (issueList.Count == 0)
        {
            return $"No Jira issues linked to pull request #{pullRequestId}.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Jira Issues for PR #{pullRequestId} ({issueList.Count})");
        sb.AppendLine(new string('-', 60));

        foreach (var issue in issueList)
        {
            sb.AppendLine($"- [{issue.Key}] {issue.Url}");
        }

        return sb.ToString();
    }
}