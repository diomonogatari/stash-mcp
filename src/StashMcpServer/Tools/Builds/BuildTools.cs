using Bitbucket.Net;
using Bitbucket.Net.Common.Mcp;
using Bitbucket.Net.Models.Builds;
using Bitbucket.Net.Models.Core.Projects;
using ModelContextProtocol.Server;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class BuildTools(
    ILogger<BuildTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    IBitbucketClient client,
    IServerSettings serverSettings)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    [McpServerTool(Name = "list_builds"), Description("List recent builds for a repository. Shows CI/CD pipeline history across commits on a branch.")]
    public async Task<string> ListBuildsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("Branch name to get builds for (default: repository default branch).")] string? branch = null,
        [Description("Maximum number of commits to check for builds (default: 25, max: 100).")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var cappedLimit = Math.Clamp(limit, 1, 100);

        LogToolInvocation(nameof(ListBuildsAsync),
            (nameof(projectKey), normalizedProjectKey),
            (nameof(repositorySlug), normalizedSlug),
            (nameof(branch), branch),
            (nameof(limit), cappedLimit));

        cancellationToken.ThrowIfCancellationRequested();

        string branchRef;
        if (string.IsNullOrWhiteSpace(branch))
        {
            try
            {
                var defaultBranch = await Client.GetDefaultBranchAsync(normalizedProjectKey, normalizedSlug, cancellationToken)
                    .ConfigureAwait(false);
                branchRef = defaultBranch?.Id ?? "refs/heads/main";
            }
            catch
            {
                branchRef = "refs/heads/main";
            }
        }
        else
        {
            branchRef = ToolHelpers.NormalizeRef(branch);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Recent Builds for `{normalizedProjectKey}/{normalizedSlug}`");
        sb.AppendLine($"**Branch:** `{branchRef.Replace("refs/heads/", "")}`");
        sb.AppendLine();

        var cacheKey = $"repo-commits:{normalizedProjectKey}:{normalizedSlug}:{branchRef}:{cappedLimit}";
        IReadOnlyList<Commit> commitList;
        try
        {
            var paginatedCommits = await ResilientApi.ExecuteAsync(
                cacheKey,
                async _ => await Client.Commits(normalizedProjectKey, normalizedSlug, branchRef)
                    .Merges(MergeCommits.Include)
                    .StreamAsync(cancellationToken)
                    .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            commitList = paginatedCommits.Items;
        }
        catch (Exception ex)
        {
            return $"❌ Failed to retrieve commits for repository: {ex.Message}";
        }

        if (commitList.Count == 0)
        {
            sb.AppendLine("No commits found for this branch.");
            return sb.ToString();
        }

        var commitIds = commitList.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)).ToArray();
        Dictionary<string, BuildStats>? buildStats = null;

        try
        {
            buildStats = await ResilientApi.ExecuteAsync(
                $"batch-build-stats:{string.Join(",", commitIds.Take(10))}",
                async _ => await Client.GetBuildStatsForCommitsAsync(cancellationToken, commitIds!)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        var totalSuccessful = 0;
        var totalFailed = 0;
        var totalInProgress = 0;
        var commitsWithBuilds = 0;

        if (buildStats is not null)
        {
            foreach (var stats in buildStats.Values)
            {
                totalSuccessful += stats.Successful;
                totalFailed += stats.Failed;
                totalInProgress += stats.InProgress;
                if (stats.Successful + stats.Failed + stats.InProgress > 0)
                {
                    commitsWithBuilds++;
                }
            }
        }

        sb.AppendLine("## Summary");
        var overallStatus = totalFailed > 0 ? "🔴 SOME BUILDS FAILING" :
            totalInProgress > 0 ? "🟡 BUILDS IN PROGRESS" :
            totalSuccessful > 0 ? "🟢 ALL BUILDS PASSING" : "⚪ NO BUILDS";
        sb.AppendLine($"**Overall Status:** {overallStatus}");
        sb.AppendLine($"**Commits Analyzed:** {commitList.Count}");
        sb.AppendLine($"**Commits with Builds:** {commitsWithBuilds}");
        sb.AppendLine($"- ✅ Total Successful: {totalSuccessful}");
        sb.AppendLine($"- ❌ Total Failed: {totalFailed}");
        sb.AppendLine($"- ⏳ Total In Progress: {totalInProgress}");
        sb.AppendLine();

        sb.AppendLine("## Build History");
        sb.AppendLine();

        foreach (var commit in commitList.Take(cappedLimit))
        {
            var shortCommitId = commit.Id?.Length > 7 ? commit.Id[..7] : commit.Id ?? "unknown";
            var commitStats = buildStats?.GetValueOrDefault(commit.Id ?? "");

            var statusIcon = "⚪";
            var statusText = "No builds";

            if (commitStats is not null)
            {
                var total = commitStats.Successful + commitStats.Failed + commitStats.InProgress;
                if (total > 0)
                {
                    statusIcon = commitStats.Failed > 0 ? "🔴" :
                        commitStats.InProgress > 0 ? "🟡" : "🟢";
                    statusText = $"✅{commitStats.Successful} ❌{commitStats.Failed} ⏳{commitStats.InProgress}";
                }
            }

            var commitDate = commit.CommitterTimestamp != default
                ? commit.CommitterTimestamp.ToString("yyyy-MM-dd HH:mm")
                : "Unknown date";

            var messageFirstLine = commit.Message?.Split('\n').FirstOrDefault()?.Trim() ?? "No message";
            if (messageFirstLine.Length > 60)
            {
                messageFirstLine = messageFirstLine[..57] + "...";
            }

            var authorDisplay = commit.Author?.Name ?? commit.Author?.EmailAddress ?? "Unknown";

            sb.AppendLine($"### {statusIcon} `{shortCommitId}` - {messageFirstLine}");
            sb.AppendLine($"- **Date:** {commitDate}");
            sb.AppendLine($"- **Author:** {authorDisplay}");
            sb.AppendLine($"- **Builds:** {statusText}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"_Use `get_build_status` with a commit hash for detailed build information._");

        return sb.ToString();
    }

    [McpServerTool(Name = "get_build_status"), Description("Get build status information for a specific commit. Shows CI/CD pipeline results. Note: Build status is retrieved globally by commit hash and does not require project/repository context.")]
    public async Task<string> GetBuildStatusAsync(
        [Description("The full or short commit hash to get build status for.")] string commitId,
        [Description("Include summary statistics (default: true).")] bool includeStats = true,
        [Description("Maximum number of build results to return (default: 25).")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            throw new ArgumentException("Commit ID is required.", nameof(commitId));
        }

        var trimmedCommitId = commitId.Trim();
        var cappedLimit = Math.Clamp(limit, 1, 100);

        LogToolInvocation(nameof(GetBuildStatusAsync),
            (nameof(commitId), commitId),
            (nameof(includeStats), includeStats),
            (nameof(limit), cappedLimit));

        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        sb.AppendLine($"# Build Status for Commit `{(trimmedCommitId.Length > 7 ? trimmedCommitId[..7] : trimmedCommitId)}`");
        sb.AppendLine();

        if (includeStats)
        {
            try
            {
                var statsCacheKey = CacheKeys.BuildStats(trimmedCommitId);
                var stats = await ResilientApi.ExecuteAsync(
                    statsCacheKey,
                    async _ => await Client.GetBuildStatsForCommitAsync(trimmedCommitId, includeUnique: true)
                        .ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (stats is not null)
                {
                    var total = stats.Successful + stats.Failed + stats.InProgress;
                    var overallStatus = stats.Failed > 0 ? "🔴 FAILING" :
                        stats.InProgress > 0 ? "🟡 IN PROGRESS" :
                        stats.Successful > 0 ? "🟢 PASSING" : "⚪ NO BUILDS";

                    sb.AppendLine("## Summary");
                    sb.AppendLine($"**Overall Status:** {overallStatus}");
                    sb.AppendLine($"**Total Builds:** {total}");
                    sb.AppendLine($"- ✅ Successful: {stats.Successful}");
                    sb.AppendLine($"- ❌ Failed: {stats.Failed}");
                    sb.AppendLine($"- ⏳ In Progress: {stats.InProgress}");
                    sb.AppendLine();
                }
            }
            catch
            {
            }
        }

        var cacheKey = $"{CacheKeys.BuildStatus(trimmedCommitId)}:limit={cappedLimit}";
        var buildStatuses = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetBuildStatusForCommitAsync(trimmedCommitId, limit: cappedLimit)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var statusList = buildStatuses.ToList();

        if (statusList.Count == 0)
        {
            sb.AppendLine("## Build Details");
            sb.AppendLine("No build information available for this commit.");
            return sb.ToString();
        }

        sb.AppendLine("## Build Details");
        sb.AppendLine();

        var grouped = statusList
            .GroupBy(s => s.State?.ToUpperInvariant() ?? "UNKNOWN")
            .OrderBy(g => g.Key switch
            {
                "FAILED" => 0,
                "INPROGRESS" => 1,
                "SUCCESSFUL" => 2,
                _ => 3
            });

        foreach (var group in grouped)
        {
            var icon = group.Key switch
            {
                "SUCCESSFUL" => "✅",
                "FAILED" => "❌",
                "INPROGRESS" => "⏳",
                _ => "❔"
            };

            sb.AppendLine($"### {icon} {group.Key} ({group.Count()})");
            sb.AppendLine();

            foreach (var build in group.OrderBy(b => b.Name))
            {
                sb.AppendLine($"**{build.Name ?? "Unnamed Build"}**");

                if (!string.IsNullOrWhiteSpace(build.Description))
                {
                    sb.AppendLine($"  - {build.Description}");
                }

                if (!string.IsNullOrWhiteSpace(build.Url))
                {
                    sb.AppendLine($"  - [View Build]({build.Url})");
                }

                if (build.DateAdded > 0)
                {
                    var dateAdded = DateTimeOffset.FromUnixTimeMilliseconds(build.DateAdded);
                    sb.AppendLine($"  - Added: {dateAdded:yyyy-MM-dd HH:mm}");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_pull_request_build_status"), Description("Get build status for the head commit of a pull request.")]
    public async Task<string> GetPullRequestBuildStatusAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestBuildStatusAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId));

        cancellationToken.ThrowIfCancellationRequested();

        var commits = await ResilientApi.ExecuteAsync(
            $"pr-commits:{normalizedProjectKey}:{normalizedSlug}:{pullRequestId}",
            async _ => await Client.GetPullRequestCommitsAsync(normalizedProjectKey, normalizedSlug, pullRequestId, limit: 1)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var headCommit = commits.FirstOrDefault()?.Id;
        if (string.IsNullOrWhiteSpace(headCommit))
        {
            return $"Unable to determine head commit for pull request #{pullRequestId}. The PR may have no commits.";
        }

        return await GetBuildStatusAsync(headCommit, includeStats: true, limit: 25, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}