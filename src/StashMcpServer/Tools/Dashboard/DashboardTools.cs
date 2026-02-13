using Bitbucket.Net;
using Bitbucket.Net.Common.Mcp;
using Bitbucket.Net.Models.Core.Projects;
using ModelContextProtocol.Server;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class DashboardTools(
    ILogger<DashboardTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    IBitbucketClient client,
    IServerSettings serverSettings)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    [McpServerTool(Name = "get_my_pull_requests"), Description("Get pull requests authored by, reviewing, or participating in by the current user across all repositories.")]
    public async Task<string> GetMyPullRequestsAsync(
        [Description("Filter by role: 'AUTHOR', 'REVIEWER', or 'PARTICIPANT'. Default is 'AUTHOR'.")] string role = "AUTHOR",
        [Description("Filter by state: 'OPEN', 'MERGED', 'DECLINED', or 'ALL'. Default is 'OPEN'.")] string state = "OPEN",
        [Description("Maximum number of pull requests to return. Default is 25.")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(nameof(GetMyPullRequestsAsync), (nameof(role), role), (nameof(state), state), (nameof(limit), limit));

        var cappedLimit = Math.Clamp(limit, 1, 100);
        var roleUpper = role.ToUpperInvariant();
        var stateUpper = state.ToUpperInvariant();

        cancellationToken.ThrowIfCancellationRequested();

        var parsedRole = Enum.TryParse<Roles>(roleUpper, ignoreCase: true, out var r) ? r : (Roles?)null;
        var parsedState = Enum.TryParse<PullRequestStates>(stateUpper, ignoreCase: true, out var s) ? s : (PullRequestStates?)null;

        var cacheKey = $"dashboard-prs:{roleUpper}:{stateUpper}:{cappedLimit}";
        var paginatedPrs = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetDashboardPullRequestsStreamAsync(
                    role: parsedRole,
                    state: parsedState,
                    cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var prList = paginatedPrs.Items;

        if (prList.Count == 0)
        {
            return $"No pull requests found for role '{roleUpper}' with state '{stateUpper}'.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# My Pull Requests (Role: {roleUpper}, State: {stateUpper})");
        sb.AppendLine($"Showing {prList.Count} result(s)");
        sb.AppendLine();

        foreach (var pr in prList)
        {
            var repoRef = pr.ToRef?.Repository;
            var repoIdentifier = repoRef != null
                ? $"{repoRef.Project?.Key}/{repoRef.Slug}"
                : "(unknown repo)";

            sb.AppendLine($"## #{pr.Id}: {pr.Title}");
            sb.AppendLine($"  Repository: {repoIdentifier}");
            sb.AppendLine($"  Author: {pr.Author?.User?.DisplayName ?? "Unknown"}");
            sb.AppendLine($"  State: {pr.State}");
            sb.AppendLine($"  {ToolHelpers.FormatBranchRef(pr.FromRef?.Id)} → {ToolHelpers.FormatBranchRef(pr.ToRef?.Id)}");

            var reviewerCount = pr.Reviewers?.Count ?? 0;
            var approvedCount = pr.Reviewers?.Count(r => r.Approved) ?? 0;
            sb.AppendLine($"  Reviewers: {approvedCount}/{reviewerCount} approved");
            sb.AppendLine();
        }

        if (paginatedPrs.HasMore)
        {
            sb.AppendLine("_More results available. Increase limit for more._");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_inbox_pull_requests"), Description("Get pull requests in the current user's review inbox - PRs awaiting their attention.")]
    public async Task<string> GetInboxPullRequestsAsync(
        [Description("Filter by role in the PR: 'REVIEWER' (default) or empty for all inbox items.")] string role = "REVIEWER",
        [Description("Maximum number of pull requests to return. Default is 25.")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        LogToolInvocation(nameof(GetInboxPullRequestsAsync), (nameof(role), role), (nameof(limit), limit));

        var cappedLimit = Math.Clamp(limit, 1, 100);

        cancellationToken.ThrowIfCancellationRequested();

        var parsedRole = Enum.TryParse<Roles>(role, ignoreCase: true, out var r) ? r : Roles.Reviewer;

        var cacheKey = $"inbox-prs:{role}:{cappedLimit}";
        var paginatedPrs = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetInboxPullRequestsStreamAsync(
                    role: parsedRole,
                    cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var prList = paginatedPrs.Items;

        if (prList.Count == 0)
        {
            return "Your review inbox is empty. No pull requests are waiting for your attention.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Review Inbox");
        sb.AppendLine($"**{prList.Count} pull request(s) awaiting your attention**");
        sb.AppendLine();

        foreach (var pr in prList)
        {
            var repoRef = pr.ToRef?.Repository;
            var repoIdentifier = repoRef != null
                ? $"{repoRef.Project?.Key}/{repoRef.Slug}"
                : "(unknown repo)";

            sb.AppendLine($"## #{pr.Id}: {pr.Title}");
            sb.AppendLine($"  Repository: {repoIdentifier}");
            sb.AppendLine($"  Author: {pr.Author?.User?.DisplayName ?? "Unknown"}");
            sb.AppendLine($"  {ToolHelpers.FormatBranchRef(pr.FromRef?.Id)} → {ToolHelpers.FormatBranchRef(pr.ToRef?.Id)}");

            var reviewerCount = pr.Reviewers?.Count ?? 0;
            var approvedCount = pr.Reviewers?.Count(r => r.Approved) ?? 0;
            sb.AppendLine($"  Reviewers: {approvedCount}/{reviewerCount} approved");
            sb.AppendLine();
        }

        if (paginatedPrs.HasMore)
        {
            sb.AppendLine("_More items in inbox. Increase limit for more._");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_recent_repositories"), Description("Get repositories the current user has recently accessed.")]
    public Task<string> GetRecentRepositoriesAsync()
    {
        LogToolInvocation(nameof(GetRecentRepositoriesAsync));

        var recentRepos = CacheService.GetRecentRepositories().ToList();

        if (recentRepos.Count == 0)
        {
            return Task.FromResult("No recent repositories found. This feature requires user context to be available.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Recent Repositories ({recentRepos.Count})");
        sb.AppendLine(new string('-', 60));

        foreach (var repo in recentRepos)
        {
            var projectKey = repo.Project?.Key ?? "?";
            sb.AppendLine($"- {projectKey}/{repo.Slug}: {repo.Name ?? repo.Slug}");
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool(Name = "get_server_info"), Description("Get information about the Bitbucket Server instance and cache status.")]
    public Task<string> GetServerInfoAsync()
    {
        LogToolInvocation(nameof(GetServerInfoAsync));

        var sb = new StringBuilder();
        sb.AppendLine("Bitbucket Server Information");
        sb.AppendLine(new string('=', 60));

        sb.AppendLine();
        sb.AppendLine("Server:");
        sb.AppendLine($"  Version: {CacheService.GetServerVersion() ?? "Unknown"}");
        sb.AppendLine($"  Display Name: {CacheService.GetServerDisplayName() ?? "Unknown"}");
        sb.AppendLine($"  Build: {CacheService.GetBuildNumber() ?? "Unknown"}");

        sb.AppendLine();
        sb.AppendLine("Current User:");
        var currentUser = CacheService.GetCurrentUser();
        if (currentUser is not null)
        {
            sb.AppendLine($"  Name: {currentUser.DisplayName}");
            sb.AppendLine($"  Username: {currentUser.Name}");
            sb.AppendLine($"  Slug: {currentUser.Slug}");
            sb.AppendLine($"  Email: {currentUser.EmailAddress ?? "Not set"}");
            sb.AppendLine($"  Active: {currentUser.Active}");
        }
        else
        {
            sb.AppendLine("Current User: Not identified");
        }

        sb.AppendLine();
        sb.AppendLine("Cache Statistics:");
        sb.AppendLine($"  Projects: {CacheService.GetProjects().Count()}");
        sb.AppendLine($"  Repositories: {CacheService.GetAllRepositories().Count()}");
        sb.AppendLine($"  Recent Repos: {CacheService.GetRecentRepositories().Count()}");

        sb.AppendLine();
        sb.AppendLine("Capabilities:");
        sb.AppendLine($"  Server-side Code Search: {(CacheService.IsSearchAvailable() ? "Available (Elasticsearch)" : "Not available (using grep fallback)")}");

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool(Name = "get_current_user"), Description("Get information about the currently authenticated user.")]
    public Task<string> GetCurrentUserAsync()
    {
        LogToolInvocation(nameof(GetCurrentUserAsync));

        var currentUser = CacheService.GetCurrentUser();
        if (currentUser is null)
        {
            return Task.FromResult("Unable to identify current user. User context not available.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Current User");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Display Name: {currentUser.DisplayName}");
        sb.AppendLine($"Username: {currentUser.Name}");
        sb.AppendLine($"Slug: {currentUser.Slug}");
        sb.AppendLine($"Email: {currentUser.EmailAddress ?? "Not set"}");
        sb.AppendLine($"Active: {currentUser.Active}");
        sb.AppendLine($"Type: {currentUser.Type ?? "Unknown"}");

        return Task.FromResult(sb.ToString());
    }
}