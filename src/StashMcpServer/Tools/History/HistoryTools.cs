using Bitbucket.Net;
using Bitbucket.Net.Common.Mcp;
using Bitbucket.Net.Models.Core.Projects;
using ModelContextProtocol.Server;
using StashMcpServer.Formatting;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class HistoryTools(
    ILogger<HistoryTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    BitbucketClient client,
    IServerSettings serverSettings,
    IDiffFormatter diffFormatter)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    [McpServerTool(Name = "get_commit"), Description("Get details of a specific commit.")]
    public async Task<string> GetCommitAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The hash of the commit to retrieve.")] string commitId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            throw new ArgumentException("Commit ID is required.", nameof(commitId));
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetCommitAsync), (nameof(projectKey), projectKey), (nameof(repositorySlug), repositorySlug), (nameof(commitId), commitId));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CacheKeys.CommitDetails(normalizedProjectKey, normalizedSlug, commitId.Trim());
        var commit = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetCommitAsync(normalizedProjectKey, normalizedSlug, commitId.Trim(), string.Empty)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (commit == null)
        {
            return $"Commit {commitId} was not found in {normalizedProjectKey}/{normalizedSlug}.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Commit {commit.DisplayId} ({commit.Id})");
        sb.AppendLine($"Author: {commit.Author?.Name} <{commit.Author?.EmailAddress}>");
        sb.AppendLine($"Date: {commit.AuthorTimestamp:u}");
        sb.AppendLine($"Committer: {commit.Committer?.Name} <{commit.Committer?.EmailAddress}> at {commit.CommitterTimestamp:u}");
        sb.AppendLine("Message:");
        sb.AppendLine(commit.Message);

        if (commit.Parents is { Count: > 0 })
        {
            sb.AppendLine("Parents:");
            foreach (var parent in commit.Parents)
            {
                sb.AppendLine($"- {parent.DisplayId} ({parent.Id})");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list_commits"), Description("List commits in a repository. Returns commit history for a branch, tag, or from a specific commit.")]
    public async Task<string> ListCommitsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("Branch name, tag, or commit hash to start listing from (default: repository's default branch).")] string? @ref = null,
        [Description("Optional file or directory path to filter commits affecting that path.")] string? path = null,
        [Description("Include merge commits: 'include' (default), 'exclude', or 'only'.")] string merges = "include",
        [Description("Maximum number of commits to return (default: 25, max: 100).")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var cappedLimit = Math.Clamp(limit, 1, 100);

        // Determine the ref to use - default to repository's default branch if not specified
        var targetRef = @ref;
        if (string.IsNullOrWhiteSpace(targetRef))
        {
            // Use cached default branch name if available, otherwise fallback to "master"
            targetRef = await CacheService.GetDefaultBranchNameAsync(normalizedProjectKey, normalizedSlug, cancellationToken).ConfigureAwait(false) ?? "master";
        }
        else
        {
            targetRef = ToolHelpers.NormalizeRef(targetRef, allowPlainCommit: true);
        }

        // Parse merge commit handling
        var mergeHandling = merges?.ToLowerInvariant() switch
        {
            "exclude" => MergeCommits.Exclude,
            "only" => MergeCommits.Only,
            _ => MergeCommits.Include
        };

        LogToolInvocation(nameof(ListCommitsAsync),
            (nameof(projectKey), normalizedProjectKey),
            (nameof(repositorySlug), normalizedSlug),
            (nameof(@ref), targetRef),
            (nameof(path), path),
            (nameof(merges), merges),
            (nameof(limit), cappedLimit));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"{CacheKeys.CommitList(normalizedProjectKey, normalizedSlug, targetRef, path)}:merges={merges}:limit={cappedLimit}";
        var paginatedCommits = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetCommitsStreamAsync(
                    normalizedProjectKey,
                    normalizedSlug,
                    until: targetRef,
                    path: path,
                    merges: mergeHandling,
                    cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var commits = paginatedCommits.Items.ToList();
        if (commits.Count == 0)
        {
            return $"No commits found in {normalizedProjectKey}/{normalizedSlug} for ref `{targetRef}`" +
                   (string.IsNullOrWhiteSpace(path) ? "." : $" affecting path `{path}`.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Commits in {normalizedProjectKey}/{normalizedSlug} (ref: `{targetRef}`)");
        if (!string.IsNullOrWhiteSpace(path))
        {
            sb.AppendLine($"Filtered by path: `{path}`");
        }

        sb.AppendLine(new string('-', 60));

        foreach (var commit in commits)
        {
            var messageFirstLine = commit.Message?.Split('\n', 2)[0] ?? "(no message)";
            if (messageFirstLine.Length > 72)
            {
                messageFirstLine = messageFirstLine[..69] + "...";
            }

            sb.AppendLine($"`{commit.DisplayId}` {commit.AuthorTimestamp:yyyy-MM-dd} {commit.Author?.Name ?? "Unknown"}");
            sb.AppendLine($"  {messageFirstLine}");
        }

        if (paginatedCommits.HasMore)
        {
            sb.AppendLine();
            sb.AppendLine($"Showing {commits.Count} commits — more available. Increase limit for more results.");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_commit_changes"), Description("View the files changed in a specific commit.")]
    public async Task<string> GetCommitChangesAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The hash of the commit.")] string commitId,
        [Description("Maximum number of changed files to return (default 100).")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            throw new ArgumentException("Commit ID is required.", nameof(commitId));
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var cappedLimit = Math.Clamp(limit, 1, 1000);

        LogToolInvocation(nameof(GetCommitChangesAsync), (nameof(projectKey), projectKey), (nameof(repositorySlug), repositorySlug), (nameof(commitId), commitId), (nameof(limit), limit));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"{CacheKeys.CommitChanges(normalizedProjectKey, normalizedSlug, commitId.Trim())}:limit={cappedLimit}";
        var paginatedChanges = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetCommitChangesStreamAsync(
                    normalizedProjectKey,
                    normalizedSlug,
                    commitId.Trim(),
                    since: string.Empty,
                    cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var changesList = paginatedChanges.Items.ToList();
        if (changesList.Count == 0)
        {
            return $"No file changes reported for commit {commitId}.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Changes for commit {commitId} ({changesList.Count} files)");
        sb.AppendLine(new string('-', 60));

        foreach (var change in changesList)
        {
            var path = change.Path?.ToString() ?? change.SrcPath?.ToString() ?? "(unknown)";
            var changeType = change.Type ?? "MODIFIED";
            sb.AppendLine($"- {path} [{changeType}]");
        }

        if (paginatedChanges.HasMore)
        {
            sb.AppendLine();
            sb.AppendLine($"Showing {changesList.Count} of more available — increase limit for more results.");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "compare_refs"), Description("Compare the diff between two refs (branches, tags, or commits) in a repository.")]
    public async Task<string> CompareRefsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ref containing changes you want to see (e.g., the feature branch). Shows what 'from' has that 'to' does not.")] string from,
        [Description("The base ref to compare against (e.g., the target/main branch).")] string to,
        [Description("Optional source path filter to limit diff to a specific file or directory.")] string? srcPath = null,
        [Description("Number of context lines around each change (default: 3).")] int contextLines = 3,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var fromRef = ToolHelpers.NormalizeRef(from, allowPlainCommit: true);
        var toRef = ToolHelpers.NormalizeRef(to, allowPlainCommit: true);

        LogToolInvocation(nameof(CompareRefsAsync),
            (nameof(projectKey), normalizedProjectKey),
            (nameof(repositorySlug), normalizedSlug),
            (nameof(from), fromRef),
            (nameof(to), toRef),
            (nameof(srcPath), srcPath),
            (nameof(contextLines), contextLines));

        cancellationToken.ThrowIfCancellationRequested();

        var diffStream = Client.GetRepositoryCompareDiffStreamAsync(
            normalizedProjectKey, normalizedSlug,
            fromRef, toRef,
            srcPath: srcPath,
            contextLines: contextLines,
            cancellationToken: cancellationToken);

        var formatted = await diffFormatter.FormatDiffStreamAsync(
            diffStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(formatted))
        {
            return $"No differences found between `{from}` and `{to}`.";
        }

        return formatted;
    }

    /// <summary>
    /// Workflow-oriented tool that provides complete commit context in a single call.
    /// Useful when investigating a specific commit's impact and changes.
    /// </summary>
    [McpServerTool(Name = "get_commit_context")]
    [Description("Get complete commit details including metadata, changed files, and optionally the diff. Use this to understand a commit's full impact.")]
    public async Task<string> GetCommitContextAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The commit hash or short hash.")] string commitId,
        [Description("Include file-level diff content (default: false). Set to true for detailed code review.")] bool includeDiff = false,
        [Description("Number of context lines around changes when includeDiff is true (default: 3).")] int contextLines = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            throw new ArgumentException("Commit ID is required.", nameof(commitId));
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var trimmedCommitId = commitId.Trim();
        var cappedContextLines = Math.Clamp(contextLines, 0, 10);

        LogToolInvocation(nameof(GetCommitContextAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(commitId), commitId),
            (nameof(includeDiff), includeDiff));

        cancellationToken.ThrowIfCancellationRequested();

        // Parallel fetch commit metadata and changes
        var commitCacheKey = CacheKeys.CommitDetails(normalizedProjectKey, normalizedSlug, trimmedCommitId);
        var commitTask = ResilientApi.ExecuteAsync(
            commitCacheKey,
            async _ => await Client.GetCommitAsync(normalizedProjectKey, normalizedSlug, trimmedCommitId, string.Empty)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken);

        var changesCacheKey = CacheKeys.CommitChanges(normalizedProjectKey, normalizedSlug, trimmedCommitId);
        var changesTask = ResilientApi.ExecuteAsync(
            changesCacheKey,
            async _ => await Client.GetCommitChangesStreamAsync(
                    normalizedProjectKey,
                    normalizedSlug,
                    trimmedCommitId,
                    since: string.Empty,
                    cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(500, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken);

        await Task.WhenAll(commitTask, changesTask).ConfigureAwait(false);

        var commit = await commitTask.ConfigureAwait(false);
        var paginatedChanges = await changesTask.ConfigureAwait(false);
        var changes = paginatedChanges.Items.ToList();

        if (commit == null)
        {
            return $"Commit `{trimmedCommitId}` was not found in {normalizedProjectKey}/{normalizedSlug}.";
        }

        // Build comprehensive response
        var sb = new StringBuilder();

        // Section 1: Commit Header
        sb.AppendLine($"# Commit: {commit.DisplayId}");
        sb.AppendLine();
        sb.AppendLine($"**Full Hash:** `{commit.Id}`");
        sb.AppendLine($"**Author:** {commit.Author?.Name ?? "Unknown"} <{commit.Author?.EmailAddress ?? "unknown"}>");
        sb.AppendLine($"**Date:** {commit.AuthorTimestamp:yyyy-MM-dd HH:mm:ss}");

        if (commit.Committer?.Name != commit.Author?.Name)
        {
            sb.AppendLine($"**Committer:** {commit.Committer?.Name ?? "Unknown"} <{commit.Committer?.EmailAddress ?? "unknown"}>");
            sb.AppendLine($"**Commit Date:** {commit.CommitterTimestamp:yyyy-MM-dd HH:mm:ss}");
        }

        sb.AppendLine();

        // Section 2: Parent commits
        if (commit.Parents is { Count: > 0 })
        {
            sb.AppendLine("## Parents");
            foreach (var parent in commit.Parents)
            {
                sb.AppendLine($"- `{parent.DisplayId}` ({parent.Id})");
            }

            sb.AppendLine();
        }

        // Section 3: Commit Message
        sb.AppendLine("## Message");
        sb.AppendLine("```");
        sb.AppendLine(commit.Message ?? "(no message)");
        sb.AppendLine("```");
        sb.AppendLine();

        // Section 4: Changed Files Summary
        sb.AppendLine($"## Changed Files ({changes.Count})");

        if (changes.Count == 0)
        {
            sb.AppendLine("No file changes detected.");
        }
        else
        {
            // Group by change type
            var grouped = changes
                .GroupBy(c => c.Type ?? "MODIFIED")
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var icon = group.Key switch
                {
                    "ADD" => "➕",
                    "DELETE" => "➖",
                    "MODIFY" => "📝",
                    "MOVE" => "📦",
                    "COPY" => "📋",
                    _ => "📄"
                };

                sb.AppendLine();
                sb.AppendLine($"### {icon} {group.Key} ({group.Count()})");

                foreach (var change in group.Take(50))
                {
                    var path = change.Path?.ToString() ?? change.SrcPath?.ToString() ?? "(unknown)";

                    if (change.Type == "MOVE" && change.SrcPath != null)
                    {
                        sb.AppendLine($"- `{change.SrcPath}` → `{path}`");
                    }
                    else
                    {
                        sb.AppendLine($"- `{path}`");
                    }
                }

                if (group.Count() > 50)
                {
                    sb.AppendLine($"*... and {group.Count() - 50} more files*");
                }
            }
        }

        if (paginatedChanges.HasMore)
        {
            sb.AppendLine();
            sb.AppendLine($"*Showing {changes.Count} changed files — more available.*");
        }

        // Section 5: Full Diff (optional)
        if (includeDiff && changes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Diff");

            try
            {
                // Use streaming API for memory efficiency on large diffs
                var diffStream = Client.GetCommitDiffStreamAsync(
                    normalizedProjectKey,
                    normalizedSlug,
                    trimmedCommitId,
                    contextLines: cappedContextLines,
                    cancellationToken: cancellationToken);

                var formattedDiff = await diffFormatter.FormatDiffStreamAsync(diffStream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                sb.AppendLine(formattedDiff);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch diff for commit {CommitId}", trimmedCommitId);
                sb.AppendLine($"*Unable to fetch diff: {ex.Message}*");
            }
        }

        return sb.ToString();
    }
}