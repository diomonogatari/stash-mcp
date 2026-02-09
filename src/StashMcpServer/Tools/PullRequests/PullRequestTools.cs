using Bitbucket.Net;
using Bitbucket.Net.Common.Mcp;
using Bitbucket.Net.Models.Core.Projects;
using Bitbucket.Net.Models.Core.Tasks;
using ModelContextProtocol.Server;
using StashMcpServer.Formatting;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class PullRequestTools(
    ILogger<PullRequestTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    BitbucketClient client,
    IServerSettings serverSettings,
    IDiffFormatter diffFormatter) : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    #region Core PR Operations

    [McpServerTool(Name = "get_pull_request_diff"), Description("Gets the diff text for a specific Bitbucket Server pull request.")]
    public async Task<string> GetPullRequestDiffAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestDiffAsync), (nameof(projectKey), projectKey), (nameof(repositorySlug), repositorySlug), (nameof(pullRequestId), pullRequestId));
        Logger.LogInformation(
            "Requesting streaming diff for PR {PullRequestId} in {ProjectKey}/{RepositorySlug}",
            pullRequestId,
            normalizedProjectKey,
            normalizedSlug);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Use streaming JSON diff when available (preferred)
            var diffStream = Client.GetPullRequestDiffStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                pullRequestId,
                cancellationToken: cancellationToken);

            // DiffFormatter handles streaming truncation
            return await diffFormatter.FormatDiffStreamAsync(diffStream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Streaming JSON diff failed for PR {PullRequestId} in {ProjectKey}/{RepositorySlug}. Falling back to raw diff text.",
                pullRequestId,
                normalizedProjectKey,
                normalizedSlug);

            var rawDiffStream = Client.GetPullRequestDiffStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                pullRequestId,
                withComments: false,
                cancellationToken: cancellationToken);

            return await diffFormatter.FormatDiffStreamAsync(rawDiffStream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [McpServerTool(Name = "list_pull_requests"), Description("Lists pull requests for a repository with optional filtering.")]
    public async Task<string> ListPullRequestsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The state of the PRs to return (OPEN, MERGED, DECLINED, ALL). Default is OPEN.")] string state = "OPEN",
        [Description("The limit of results to return. Default is 25.")] int limit = 25,
        [Description("Return minimal output (compact format with essential fields only). Default is false.")] bool minimalOutput = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var cappedLimit = ResponseTruncation.ClampLimit(limit);

        LogToolInvocation(nameof(ListPullRequestsAsync), (nameof(projectKey), projectKey), (nameof(repositorySlug), repositorySlug), (nameof(state), state), (nameof(limit), limit), (nameof(minimalOutput), minimalOutput));
        Logger.LogInformation(
            "Listing PRs for {ProjectKey}/{RepositorySlug} with state {State}",
            normalizedProjectKey,
            normalizedSlug,
            state);

        var prState = PullRequestStates.Open;
        if (Enum.TryParse(state, true, out PullRequestStates parsedState))
        {
            prState = parsedState;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"{CacheKeys.PullRequestList(normalizedProjectKey, normalizedSlug, state.ToUpperInvariant())}:limit={cappedLimit}";
        var paginatedPrs = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetPullRequestsStreamAsync(
                    normalizedProjectKey,
                    normalizedSlug,
                    state: prState,
                    cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var prList = paginatedPrs.Items.ToList();

        // Minimal output mode - compact format
        if (minimalOutput)
        {
            return MinimalOutputFormatter.FormatPullRequests(prList, normalizedProjectKey, normalizedSlug, state.ToUpperInvariant());
        }

        // Standard detailed output
        var sb = new StringBuilder();
        sb.AppendLine($"Pull Requests for {normalizedProjectKey}/{normalizedSlug} ({state.ToUpperInvariant()})");
        sb.AppendLine("--------------------------------------------------");

        foreach (var pr in prList)
        {
            sb.AppendLine($"#{pr.Id}: {pr.Title}");

            var authorName = pr.Author?.User?.DisplayName ?? "Unknown";
            var isCurrentUser = CacheService.IsCurrentUser(pr.Author?.User?.Slug);
            var youMarker = isCurrentUser ? " (you)" : string.Empty;
            sb.AppendLine($"  Author: {authorName}{youMarker}");

            sb.AppendLine($"  State: {pr.State}");
            sb.AppendLine($"  Created: {pr.CreatedDate}");
            var selfLinks = pr.Links?.Self;

            if (selfLinks is { Count: > 0 })
            {
                sb.AppendLine($"  Link: {selfLinks[0].Href}");
            }
            else
            {
                sb.AppendLine("  Link: (No self link available)");
            }
            sb.AppendLine();
        }

        if (prList.Count == 0)
        {
            sb.AppendLine("No pull requests found.");
        }

        if (paginatedPrs.HasMore)
        {
            sb.AppendLine("More pull requests available. Increase limit or narrow filters.");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get_pull_request"), Description("Gets detailed metadata for a specific pull request.")]
    public async Task<string> GetPullRequestDetailsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestDetailsAsync), (nameof(projectKey), projectKey), (nameof(repositorySlug), repositorySlug), (nameof(pullRequestId), pullRequestId));
        Logger.LogInformation(
            "Getting details for PR {PullRequestId} in {ProjectKey}/{RepositorySlug}",
            pullRequestId,
            normalizedProjectKey,
            normalizedSlug);

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CacheKeys.PullRequestDetails(normalizedProjectKey, normalizedSlug, pullRequestId);
        var pr = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetPullRequestAsync(normalizedProjectKey, normalizedSlug, pullRequestId)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (pr is null)
        {
            return $"Pull request #{pullRequestId} not found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Pull Request #{pr.Id}: {pr.Title}");
        sb.AppendLine($"State: {pr.State}");

        var isAuthor = CacheService.IsCurrentUser(pr.Author?.User?.Slug);
        var authorMarker = isAuthor ? " (you)" : string.Empty;
        sb.AppendLine($"Author: {pr.Author?.User?.DisplayName} ({pr.Author?.User?.EmailAddress}){authorMarker}");

        sb.AppendLine($"Created: {pr.CreatedDate}");
        sb.AppendLine($"Updated: {pr.UpdatedDate}");
        sb.AppendLine($"Source: {pr.FromRef?.Id} (Repo: {pr.FromRef?.Repository?.Slug})");
        sb.AppendLine($"Target: {pr.ToRef?.Id} (Repo: {pr.ToRef?.Repository?.Slug})");
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine("Description:");
        sb.AppendLine(pr.Description ?? "(No description)");
        sb.AppendLine("--------------------------------------------------");
        if (pr.Reviewers?.Any() == true)
        {
            sb.AppendLine("Reviewers:");
            foreach (var reviewer in pr.Reviewers)
            {
                var isReviewer = CacheService.IsCurrentUser(reviewer.User?.Slug);
                var reviewerMarker = isReviewer ? " (you)" : string.Empty;
                sb.AppendLine($"- {reviewer.User?.DisplayName}: {reviewer.Status} (Approved: {reviewer.Approved}){reviewerMarker}");
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Comments

    [McpServerTool(Name = "get_pull_request_comments"), Description("Gets all comments for a specific pull request, including code context and nested replies.")]
    public async Task<string> GetPullRequestCommentsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        [Description("Optional filter by anchor state: 'ACTIVE' (default), 'ORPHANED', or 'ALL'.")] string anchorState = "ALL",
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestCommentsAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId),
            (nameof(anchorState), anchorState));

        Logger.LogInformation(
            "Getting comments for PR {PullRequestId} in {ProjectKey}/{RepositorySlug}",
            pullRequestId,
            normalizedProjectKey,
            normalizedSlug);

        var parsedAnchorState = ParseAnchorState(anchorState);

        cancellationToken.ThrowIfCancellationRequested();

        var activitiesCacheKey = CacheKeys.PullRequestActivities(normalizedProjectKey, normalizedSlug, pullRequestId);
        var paginatedActivities = await ResilientApi.ExecuteAsync(
            activitiesCacheKey,
            async _ => await Client.GetPullRequestActivitiesStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                (long)pullRequestId,
                cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(500, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var commentActivities = paginatedActivities.Items
            .Where(a => a.Comment is not null)
            .ToList();

        if (commentActivities.Count == 0)
        {
            return $"No comments found for pull request #{pullRequestId}.";
        }

        var codeContextCache = new Dictionary<CodeContextCacheKey, string?>();

        var sb = new StringBuilder();
        sb.AppendLine($"Comments for Pull Request #{pullRequestId}");
        sb.AppendLine("==================================================");

        sb.AppendLine();

        var commentCount = 0;
        foreach (var activity in commentActivities)
        {
            var comment = activity.Comment!;
            var anchor = activity.CommentAnchor;

            if (!MatchesAnchorState(anchor, parsedAnchorState))
            {
                continue;
            }

            commentCount++;

            var codeContext = anchor is not null
                ? await GetCodeContextForAnchorAsync(normalizedProjectKey, normalizedSlug, anchor, codeContextCache, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            FormatComment(sb, comment, anchor, codeContext, indent: 0);
        }

        if (commentCount == 0)
        {
            return $"No comments matching filter '{anchorState}' found for pull request #{pullRequestId}.";
        }

        sb.AppendLine("==================================================");
        sb.AppendLine($"Total comments: {commentCount}");

        return sb.ToString();
    }

    [McpServerTool(Name = "get_pull_request_unresolved_comments"), Description("Gets only the unresolved/active comments for a pull request, typically representing open discussions or issues to address.")]
    public async Task<string> GetPullRequestUnresolvedCommentsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestUnresolvedCommentsAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId));

        Logger.LogInformation(
            "Getting unresolved comments for PR {PullRequestId} in {ProjectKey}/{RepositorySlug}",
            pullRequestId,
            normalizedProjectKey,
            normalizedSlug);

        cancellationToken.ThrowIfCancellationRequested();

        var activitiesCacheKey = CacheKeys.PullRequestActivities(normalizedProjectKey, normalizedSlug, pullRequestId);
        var paginatedActivities = await ResilientApi.ExecuteAsync(
            activitiesCacheKey,
            async _ => await Client.GetPullRequestActivitiesStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                (long)pullRequestId,
                cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(500, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var commentActivities = paginatedActivities.Items
            .Where(a => a.Comment is not null)
            .ToList();

        if (commentActivities.Count == 0)
        {
            return $"No comments found for pull request #{pullRequestId}.";
        }

        var codeContextCache = new Dictionary<CodeContextCacheKey, string?>();

        // Fetch tasks with graceful degradation - task API failure should not block comment retrieval
        var (tasks, tasksError) = await GetTasksSafeAsync(normalizedProjectKey, normalizedSlug, pullRequestId, cancellationToken)
            .ConfigureAwait(false);

        var resolvedCommentIds = tasks
            .Where(t => t.State == "RESOLVED")
            .Select(t => t.Anchor?.Id)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var sb = new StringBuilder();
        sb.AppendLine($"Unresolved Comments for Pull Request #{pullRequestId}");
        sb.AppendLine("==================================================");

        // Show warnings for unavailable context
        if (tasksError is not null)
        {
            sb.AppendLine($"⚠️ Note: Task resolution status unavailable - {tasksError}");
        }
        sb.AppendLine();

        var unresolvedCount = 0;
        foreach (var activity in commentActivities)
        {
            var comment = activity.Comment!;
            var anchor = activity.CommentAnchor;

            if (IsCommentResolved(comment, resolvedCommentIds))
            {
                continue;
            }

            if (anchor is not null && !IsActiveAnchor(anchor))
            {
                continue;
            }

            unresolvedCount++;

            var codeContext = anchor is not null
                ? await GetCodeContextForAnchorAsync(normalizedProjectKey, normalizedSlug, anchor, codeContextCache, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            FormatComment(sb, comment, anchor, codeContext, indent: 0, showOnlyUnresolved: true, resolvedCommentIds: resolvedCommentIds);
        }

        if (unresolvedCount == 0)
        {
            return $"No unresolved comments found for pull request #{pullRequestId}. All discussions have been resolved! 🎉";
        }

        sb.AppendLine("==================================================");
        sb.AppendLine($"Total unresolved comments: {unresolvedCount}");

        return sb.ToString();
    }

    [McpServerTool(Name = "reply_to_pull_request_comment"), Description("Reply to an existing comment on a pull request. Creates a threaded reply under the specified parent comment.")]
    public async Task<string> ReplyToPullRequestCommentAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        [Description("The ID of the parent comment to reply to.")] int parentCommentId,
        [Description("The reply text content.")] string text,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Reply text is required.", nameof(text));
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(ReplyToPullRequestCommentAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId),
            (nameof(parentCommentId), parentCommentId));

        Logger.LogInformation(
            "Replying to comment {CommentId} on PR {PullRequestId} in {ProjectKey}/{RepositorySlug}",
            parentCommentId,
            pullRequestId,
            normalizedProjectKey,
            normalizedSlug);

        cancellationToken.ThrowIfCancellationRequested();

        var parentComment = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.GetPullRequestCommentAsync(
                normalizedProjectKey,
                normalizedSlug,
                pullRequestId,
                parentCommentId)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (parentComment is null)
        {
            return $"Parent comment #{parentCommentId} not found on pull request #{pullRequestId}.";
        }

        var createdComment = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.CreatePullRequestCommentAsync(
                normalizedProjectKey,
                normalizedSlug,
                pullRequestId,
                text.Trim(),
                parentId: parentCommentId.ToString())
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (createdComment is null)
        {
            return "Failed to create reply. The Bitbucket API did not return the created comment.";
        }

        // Invalidate comment-related caches so subsequent reads reflect the new reply
        ResilientApi.InvalidateCache(CacheKeys.PullRequestComments(normalizedProjectKey, normalizedSlug, pullRequestId));
        ResilientApi.InvalidateCache(CacheKeys.PullRequestActivities(normalizedProjectKey, normalizedSlug, pullRequestId));
        // Invalidate all context variations (16 combinations)
        ResilientApi.InvalidateAllContextVariations(normalizedProjectKey, normalizedSlug, pullRequestId);

        var sb = new StringBuilder();
        sb.AppendLine($"Successfully replied to comment #{parentCommentId}");
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine($"Reply ID: #{createdComment.Id}");
        sb.AppendLine($"Author: {createdComment.Author?.DisplayName ?? createdComment.Author?.Name ?? "Unknown"}");
        sb.AppendLine($"Created: {createdComment.CreatedDate?.ToString("u") ?? "Unknown"}");
        sb.AppendLine();
        sb.AppendLine("Content:");
        sb.AppendLine(createdComment.Text);

        return sb.ToString();
    }

    [McpServerTool(Name = "add_pull_request_comment"), Description("Add a new comment to a pull request. Can be a general comment or attached to a specific line in a file.")]
    public async Task<string> AddPullRequestCommentAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        [Description("The comment text content.")] string text,
        [Description("Optional file path to attach the comment to a specific file.")] string? filePath = null,
        [Description("Optional line number to attach the comment to (requires filePath).")] int? line = null,
        [Description("Optional line type: 'ADDED', 'REMOVED', or 'CONTEXT' (default: 'ADDED'). Determines which version of the file the line number refers to.")] string? lineType = null,
        [Description("Optional file type: 'FROM' (source/old file) or 'TO' (destination/new file). Auto-derived from lineType if not specified: ADDED→TO, REMOVED→FROM, CONTEXT→TO.")] string? fileType = null,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Comment text is required.", nameof(text));
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(AddPullRequestCommentAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId),
            (nameof(filePath), filePath),
            (nameof(line), line));

        Logger.LogInformation(
            "Adding comment to PR {PullRequestId} in {ProjectKey}/{RepositorySlug}",
            pullRequestId,
            normalizedProjectKey,
            normalizedSlug);

        cancellationToken.ThrowIfCancellationRequested();

        LineTypes? parsedLineType = null;
        FileTypes? parsedFileType = null;
        DiffTypes? parsedDiffType = null;

        if (!string.IsNullOrWhiteSpace(lineType))
        {
            parsedLineType = lineType.ToUpperInvariant() switch
            {
                "ADDED" => LineTypes.Added,
                "REMOVED" => LineTypes.Removed,
                "CONTEXT" => LineTypes.Context,
                _ => LineTypes.Added
            };
        }
        else if (line.HasValue)
        {
            parsedLineType = LineTypes.Added;
        }

        // Parse explicit fileType if provided, otherwise derive from lineType
        // Bitbucket Server requires fileType for line-specific comments:
        // - FROM: refers to the source (old) file in the diff
        // - TO: refers to the destination (new) file in the diff
        if (!string.IsNullOrWhiteSpace(fileType))
        {
            parsedFileType = fileType.ToUpperInvariant() switch
            {
                "FROM" => FileTypes.From,
                "TO" => FileTypes.To,
                _ => null
            };
        }
        else if (line.HasValue && parsedLineType.HasValue)
        {
            // Auto-derive fileType from lineType:
            // - ADDED lines exist only in the new file (TO)
            // - REMOVED lines exist only in the old file (FROM)
            // - CONTEXT lines exist in both, default to new file (TO)
            parsedFileType = parsedLineType.Value switch
            {
                LineTypes.Added => FileTypes.To,
                LineTypes.Removed => FileTypes.From,
                LineTypes.Context => FileTypes.To, // Default to new file for context lines
                _ => FileTypes.To
            };
        }

        // Set diffType to EFFECTIVE for anchored comments (file or line-specific)
        // This tells Bitbucket to use the effective diff (merged view)
        // diffType is required when fromHash/toHash are provided
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            parsedDiffType = DiffTypes.Effective;
        }

        // For file/line comments, we need the commit hashes from the PR diff
        // Without these, Bitbucket Server ignores the anchor and creates a general comment
        string? fromHash = null;
        string? toHash = null;
        string? diffWarning = null;

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            // Get commit hashes needed for file/line anchoring via merge-base API and PR refs
            try
            {
                // Get merge base for fromHash (the common ancestor)
                var mergeBase = await ResilientApi.ExecuteAsync(
                    CacheKeys.PullRequestMergeBase(normalizedProjectKey, normalizedSlug, pullRequestId),
                    async _ => await Client.GetPullRequestMergeBaseAsync(normalizedProjectKey, normalizedSlug, pullRequestId)
                        .ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                // Get PR info for toHash (the source branch head)
                var pr = await ResilientApi.ExecuteAsync(
                    CacheKeys.PullRequest(normalizedProjectKey, normalizedSlug, pullRequestId),
                    async _ => await Client.GetPullRequestAsync(normalizedProjectKey, normalizedSlug, pullRequestId)
                        .ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                fromHash = mergeBase?.Id;
                toHash = pr?.FromRef?.LatestCommit;

                if (!string.IsNullOrWhiteSpace(fromHash) && !string.IsNullOrWhiteSpace(toHash))
                {
                    Logger.LogDebug(
                        "Successfully retrieved commit hashes. FromHash: {FromHash}, ToHash: {ToHash}",
                        fromHash,
                        toHash);
                }
                else
                {
                    Logger.LogWarning(
                        "Could not determine commit hashes. FromHash: {FromHash}, ToHash: {ToHash}. Comment may be created as general comment.",
                        fromHash ?? "(null)",
                        toHash ?? "(null)");
                    diffWarning = "Note: Could not determine commit hashes for line anchoring. Comment may not be attached to specific line.";
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "Hash retrieval failed. Comment will be created but may not be anchored to specific line.");
                var truncatedMessage = ex.Message.Length > 100 ? ex.Message[..100] + "..." : ex.Message;
                diffWarning = $"Note: Could not retrieve commit hashes ({truncatedMessage}). Comment may not be attached to specific line.";
            }
        }

        var createdComment = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.CreatePullRequestCommentAsync(
                normalizedProjectKey,
                normalizedSlug,
                pullRequestId,
                text.Trim(),
                path: filePath,
                line: line,
                lineType: parsedLineType,
                fileType: parsedFileType,
                diffType: parsedDiffType,
                fromHash: fromHash,
                toHash: toHash)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (createdComment is null)
        {
            return "Failed to create comment. The Bitbucket API did not return the created comment.";
        }

        // Invalidate comment-related caches so subsequent reads reflect the new comment
        ResilientApi.InvalidateCache(CacheKeys.PullRequestComments(normalizedProjectKey, normalizedSlug, pullRequestId));
        ResilientApi.InvalidateCache(CacheKeys.PullRequestActivities(normalizedProjectKey, normalizedSlug, pullRequestId));
        // Invalidate all context variations (16 combinations)
        ResilientApi.InvalidateAllContextVariations(normalizedProjectKey, normalizedSlug, pullRequestId);

        var sb = new StringBuilder();
        sb.AppendLine("Successfully added comment");
        sb.AppendLine("--------------------------------------------------");

        // Show warning if line-specific comment couldn't be created
        if (!string.IsNullOrWhiteSpace(diffWarning))
        {
            sb.AppendLine($"⚠️ {diffWarning}");
            sb.AppendLine();
        }

        sb.AppendLine($"Comment ID: #{createdComment.Id}");
        sb.AppendLine($"Author: {createdComment.Author?.DisplayName ?? createdComment.Author?.Name ?? "Unknown"}");
        sb.AppendLine($"Created: {createdComment.CreatedDate?.ToString("u") ?? "Unknown"}");

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            sb.AppendLine($"File: {filePath}");
            if (line.HasValue)
            {
                sb.AppendLine($"Line: {line}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Content:");
        sb.AppendLine(createdComment.Text);

        return sb.ToString();
    }

    #endregion

    #region Context (Workflow Tool)

    /// <summary>
    /// Workflow-oriented tool that provides complete pull request context in a single call.
    /// This reduces LLM tool calls from 3+ to 1 when reviewing a pull request.
    /// Implements graceful degradation - optional sections fail independently without
    /// breaking the entire response.
    /// </summary>
    [McpServerTool(Name = "get_pull_request_context")]
    [Description("Get complete pull request details including metadata, comments, and optionally diff. Use this for comprehensive PR review context in a single call.")]
    public async Task<string> GetPullRequestContextAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        [Description("Include comments and discussions (default: true).")] bool includeComments = true,
        [Description("Include diff/changes summary (default: false). Set to true for code review.")] bool includeDiff = false,
        [Description("Include activity timeline showing approvals, merges, updates (default: false).")] bool includeActivity = false,
        [Description("Include tasks attached to the PR (default: true).")] bool includeTasks = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestContextAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId),
            (nameof(includeComments), includeComments),
            (nameof(includeDiff), includeDiff),
            (nameof(includeActivity), includeActivity),
            (nameof(includeTasks), includeTasks));

        Logger.LogInformation(
            "Getting complete context for PR {PullRequestId} in {ProjectKey}/{RepositorySlug}",
            pullRequestId,
            normalizedProjectKey,
            normalizedSlug);

        cancellationToken.ThrowIfCancellationRequested();

        // Fetch base PR data first (required) - this must succeed
        var prCacheKey = CacheKeys.PullRequestDetails(normalizedProjectKey, normalizedSlug, pullRequestId);
        var pr = await ResilientApi.ExecuteAsync(
            prCacheKey,
            async _ => await Client.GetPullRequestAsync(normalizedProjectKey, normalizedSlug, pullRequestId)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (pr is null)
        {
            return $"Pull request #{pullRequestId} not found in {normalizedProjectKey}/{normalizedSlug}.";
        }

        // Parallel fetch optional data with individual error handling for graceful degradation
        // Each optional section can fail independently without breaking the entire response
        var activitiesTask = (includeComments || includeActivity)
            ? GetActivitiesSafeAsync(normalizedProjectKey, normalizedSlug, pullRequestId, cancellationToken)
            : Task.FromResult<(List<PullRequestActivity> Activities, string? Error)>(([], null));

        var diffTask = includeDiff
            ? GetDiffSafeAsync(normalizedProjectKey, normalizedSlug, pullRequestId, cancellationToken)
            : Task.FromResult<(string? FormattedDiff, string? Error)>((null, null));

        var tasksTask = includeTasks
            ? GetTasksSafeAsync(normalizedProjectKey, normalizedSlug, pullRequestId, cancellationToken)
            : Task.FromResult<(List<BitbucketTask> Tasks, string? Error)>(([], null));

        // Wait for all tasks - exceptions are already handled internally
        await Task.WhenAll(activitiesTask, diffTask, tasksTask).ConfigureAwait(false);

        var (activities, activitiesError) = await activitiesTask.ConfigureAwait(false);
        var (formattedDiff, diffError) = await diffTask.ConfigureAwait(false);
        var (tasks, tasksError) = await tasksTask.ConfigureAwait(false);

        // Build comprehensive response with graceful degradation
        var sb = new StringBuilder();

        // Section 1: Pull Request Metadata (always included)
        FormatPullRequestMetadata(sb, pr);

        // Section 2: Reviewers Status
        if (pr.Reviewers?.Any() == true)
        {
            sb.AppendLine();
            FormatReviewersSection(sb, pr.Reviewers);
        }

        // Section 3: Tasks (with graceful degradation)
        if (includeTasks)
        {
            sb.AppendLine();
            if (tasksError is not null)
            {
                sb.AppendLine("## Tasks");
                sb.AppendLine($"⚠️ Tasks unavailable: {tasksError}");
            }
            else if (tasks.Count > 0)
            {
                FormatTasksSection(sb, tasks);
            }
            else
            {
                sb.AppendLine("## Tasks");
                sb.AppendLine("No tasks on this pull request.");
            }
        }

        // Section 4: Activity Timeline (with graceful degradation)
        if (includeActivity)
        {
            sb.AppendLine();
            if (activitiesError is not null)
            {
                sb.AppendLine("## Activity Timeline");
                sb.AppendLine($"⚠️ Activity unavailable: {activitiesError}");
            }
            else if (activities.Count > 0)
            {
                FormatActivityTimeline(sb, activities);
            }
            else
            {
                sb.AppendLine("## Activity Timeline");
                sb.AppendLine("No recent activity.");
            }
        }

        // Section 5: Comments/Discussions (with graceful degradation)
        if (includeComments)
        {
            sb.AppendLine();
            if (activitiesError is not null)
            {
                // Comments come from activities, so if activities failed we can't show comments
                sb.AppendLine("## Comments");
                sb.AppendLine($"⚠️ Comments unavailable: {activitiesError}");
            }
            else
            {
                var commentActivities = activities.Where(a => a.Comment is not null).ToList();
                if (commentActivities.Count > 0)
                {
                    FormatCommentsSection(sb, commentActivities);
                }
                else
                {
                    sb.AppendLine("## Comments");
                    sb.AppendLine("No comments on this pull request.");
                }
            }
        }

        // Section 6: Diff Summary (with graceful degradation)
        if (includeDiff)
        {
            sb.AppendLine();
            if (diffError is not null)
            {
                sb.AppendLine("## Changes Summary");
                sb.AppendLine($"⚠️ Diff unavailable: {diffError}");
            }
            else if (!string.IsNullOrEmpty(formattedDiff))
            {
                sb.AppendLine(formattedDiff);
            }
            else
            {
                sb.AppendLine("## Changes Summary");
                sb.AppendLine("No diff data available.");
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Management

    [McpServerTool(Name = "create_pull_request"), Description("Create a new pull request from the source branch into the target branch.")]
    public async Task<string> CreatePullRequestAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The title of the pull request.")] string title,
        [Description("The source branch or commit (e.g., feature/my-work or refs/heads/feature/my-work).")] string sourceRef,
        [Description("The target branch or commit (e.g., main or refs/heads/main). Defaults to the repository's default branch if not specified.")] string? targetRef = null,
        [Description("Optional pull request description.")] string? description = null,
        [Description("Comma-separated reviewer usernames or slugs.")] string? reviewers = null,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var fromRefId = ToolHelpers.NormalizeRef(sourceRef);

        var effectiveTargetRef = targetRef;
        if (string.IsNullOrWhiteSpace(effectiveTargetRef))
        {
            effectiveTargetRef = CacheService.GetDefaultBranchRef(normalizedProjectKey, normalizedSlug);
        }

        var toRefId = ToolHelpers.NormalizeRef(effectiveTargetRef);

        LogToolInvocation(nameof(CreatePullRequestAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(title), title),
            (nameof(sourceRef), sourceRef),
            (nameof(targetRef), effectiveTargetRef));

        cancellationToken.ThrowIfCancellationRequested();

        var pullRequestInfo = new PullRequestInfo
        {
            Title = title.Trim(),
            Description = description?.Trim(),
            State = PullRequestStates.Open,
            Open = true,
            Closed = false,
            FromRef = new FromToRef
            {
                Id = fromRefId,
                Repository = ToolHelpers.CreateRepositoryReference(normalizedProjectKey, normalizedSlug)
            },
            ToRef = new FromToRef
            {
                Id = toRefId,
                Repository = ToolHelpers.CreateRepositoryReference(normalizedProjectKey, normalizedSlug)
            },
            Reviewers = ToolHelpers.BuildReviewers(reviewers)
        };

        var result = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.CreatePullRequestAsync(normalizedProjectKey, normalizedSlug, pullRequestInfo)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Invalidate PR list cache so the new PR appears in subsequent list queries
        ResilientApi.InvalidatePullRequestListCache(normalizedProjectKey, normalizedSlug);

        return FormatPullRequestSummary(result);
    }

    [McpServerTool(Name = "update_pull_request"), Description("Update pull request metadata such as title, description, or reviewers.")]
    public async Task<string> UpdatePullRequestAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        [Description("Optional new title for the pull request.")] string? title = null,
        [Description("Optional new description for the pull request.")] string? description = null,
        [Description("Comma-separated reviewers to add.")] string? addReviewers = null,
        [Description("Comma-separated reviewers to remove.")] string? removeReviewers = null,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        if (string.IsNullOrWhiteSpace(title)
            && string.IsNullOrWhiteSpace(description)
            && string.IsNullOrWhiteSpace(addReviewers)
            && string.IsNullOrWhiteSpace(removeReviewers))
        {
            return "No updates specified. Provide at least one field to change.";
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(UpdatePullRequestAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId));

        cancellationToken.ThrowIfCancellationRequested();

        var existing = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.GetPullRequestAsync(normalizedProjectKey, normalizedSlug, pullRequestId)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return $"Pull request #{pullRequestId} not found.";
        }

        var reviewerNames = existing.Reviewers?
            .Select(ToolHelpers.GetReviewerIdentifier)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList() ?? new List<string>();

        var addList = ToolHelpers.ParseReviewerList(addReviewers)
            .Where(reviewer => !reviewerNames.Contains(reviewer, StringComparer.OrdinalIgnoreCase))
            .ToList();
        reviewerNames.AddRange(addList);

        var removeList = ToolHelpers.ParseReviewerList(removeReviewers).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removeList.Count > 0)
        {
            reviewerNames.RemoveAll(name => removeList.Contains(name));
        }

        var updatedTitle = string.IsNullOrWhiteSpace(title) ? existing.Title : title.Trim();
        var updatedDescription = string.IsNullOrWhiteSpace(description) ? existing.Description : description.Trim();

        var update = new PullRequestUpdate
        {
            Id = existing.Id,
            Version = existing.Version,
            Title = updatedTitle,
            Description = updatedDescription,
            Reviewers = ToolHelpers.BuildReviewers(reviewerNames)
        };

        var result = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.UpdatePullRequestAsync(normalizedProjectKey, normalizedSlug, pullRequestId, update)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Invalidate PR caches so subsequent reads reflect the changes
        ResilientApi.InvalidatePullRequestCache(normalizedProjectKey, normalizedSlug, pullRequestId);
        ResilientApi.InvalidatePullRequestListCache(normalizedProjectKey, normalizedSlug);

        return FormatPullRequestSummary(result);
    }

    [McpServerTool(Name = "approve_pull_request"), Description("Approve a pull request.")]
    public async Task<string> ApprovePullRequestAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(ApprovePullRequestAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await ResilientApi.ExecuteWithoutCacheAsync(
                async _ =>
                {
                    await Client.ApprovePullRequestAsync(normalizedProjectKey, normalizedSlug, pullRequestId)
                        .ConfigureAwait(false);
                    return true; // Return a dummy value since the method returns void
                },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return $"Approved pull request #{pullRequestId} in {normalizedProjectKey}/{normalizedSlug}.";
        }
        catch (Exception ex) when (ex.Message.Contains("Authors may not", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("author", StringComparison.OrdinalIgnoreCase) &&
                                   ex.Message.Contains("status", StringComparison.OrdinalIgnoreCase))
        {
            return $"Cannot approve pull request #{pullRequestId}: You are the author of this pull request. Only reviewers can approve.";
        }
    }

    #endregion

    #region Tasks

    [McpServerTool(Name = "get_pull_request_tasks"), Description("List all tasks associated with a pull request. Tasks are actionable items that must be resolved before merging.")]
    public async Task<string> GetPullRequestTasksAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The ID of the pull request.")] int pullRequestId,
        [Description("Optional filter by task state: 'OPEN', 'RESOLVED', or 'ALL' (default).")] string state = "ALL",
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetPullRequestTasksAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId),
            (nameof(state), state));

        cancellationToken.ThrowIfCancellationRequested();

        // Try to fetch tasks using graceful degradation - tasks endpoint may return 404 on Bitbucket Server 9.0+
        var (tasks, tasksError) = await GetTasksSafeAsync(normalizedProjectKey, normalizedSlug, pullRequestId, cancellationToken)
            .ConfigureAwait(false);

        var taskList = tasks;

        // Filter by state if specified
        var normalizedState = state?.ToUpperInvariant() ?? "ALL";
        if (normalizedState != "ALL")
        {
            taskList = taskList.Where(t => string.Equals(t.State, normalizedState, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Tasks for Pull Request #{pullRequestId}");
        sb.AppendLine(new string('=', 60));

        // Show warning if tasks API failed (e.g., 404 on Bitbucket Server 9.0+)
        if (tasksError is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"⚠️ **Note:** Tasks API unavailable - {tasksError}");
            sb.AppendLine();
            sb.AppendLine("This may indicate that the Bitbucket Server version doesn't support the legacy tasks endpoint.");
            sb.AppendLine("Consider using `get_pull_request_context` with `includeTasks=true` which provides graceful fallback.");
            return sb.ToString();
        }

        if (taskList.Count == 0)
        {
            var filterMsg = normalizedState != "ALL" ? $" with state '{normalizedState}'" : string.Empty;
            return $"No tasks found{filterMsg} for pull request #{pullRequestId}.";
        }

        sb.AppendLine();

        // Group tasks by state
        var openTasks = taskList.Where(t => string.Equals(t.State, "OPEN", StringComparison.OrdinalIgnoreCase)).ToList();
        var resolvedTasks = taskList.Where(t => string.Equals(t.State, "RESOLVED", StringComparison.OrdinalIgnoreCase)).ToList();

        sb.AppendLine($"**Summary:** {openTasks.Count} open, {resolvedTasks.Count} resolved");
        sb.AppendLine();

        if (openTasks.Count > 0)
        {
            sb.AppendLine("## ⚠️ Open Tasks");
            sb.AppendLine();
            foreach (var task in openTasks.OrderBy(t => t.CreatedDate))
            {
                FormatTask(sb, task);
            }
        }

        if (resolvedTasks.Count > 0 && normalizedState is "ALL" or "RESOLVED")
        {
            sb.AppendLine("## ✅ Resolved Tasks");
            sb.AppendLine();
            foreach (var task in resolvedTasks.OrderBy(t => t.CreatedDate))
            {
                FormatTask(sb, task);
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "create_pull_request_task"), Description("Create a new task attached to a comment on a pull request. Tasks are actionable items that must be resolved before merging. Use `get_pull_request_comments` to find comment IDs. Tasks are always anchored to existing comments - you cannot create a standalone task. When context parameters (projectKey, repositorySlug, pullRequestId) are provided, the comment will be validated to ensure it exists on the specified PR.")]
    public async Task<string> CreatePullRequestTaskAsync(
        [Description("The ID of the comment to attach the task to. Use `get_pull_request_comments` to find available comment IDs.")] int commentId,
        [Description("The task description text.")] string text,
        [Description("Optional: The Bitbucket project key for validation (e.g., 'PROJ'). When provided with repositorySlug and pullRequestId, validates the comment exists on the PR.")] string? projectKey = null,
        [Description("Optional: The repository slug for validation. When provided with projectKey and pullRequestId, validates the comment exists on the PR.")] string? repositorySlug = null,
        [Description("Optional: The pull request ID for validation. When provided with projectKey and repositorySlug, validates the comment exists on the PR.")] int? pullRequestId = null,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Task text is required.", nameof(text));
        }

        LogToolInvocation(nameof(CreatePullRequestTaskAsync),
            (nameof(commentId), commentId),
            (nameof(text), text),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(pullRequestId), pullRequestId));

        cancellationToken.ThrowIfCancellationRequested();

        // Validate comment exists if context parameters are provided
        string? normalizedProjectKey = null;
        string? normalizedSlug = null;
        if (!string.IsNullOrWhiteSpace(projectKey) && !string.IsNullOrWhiteSpace(repositorySlug) && pullRequestId.HasValue)
        {
            normalizedProjectKey = NormalizeProjectKey(projectKey);
            normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

            var comment = await ResilientApi.ExecuteWithoutCacheAsync(
                async _ => await Client.GetPullRequestCommentAsync(
                    normalizedProjectKey,
                    normalizedSlug,
                    pullRequestId.Value,
                    commentId)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (comment is null)
            {
                return $"Comment #{commentId} not found on pull request #{pullRequestId} in {normalizedProjectKey}/{normalizedSlug}. " +
                       $"Use `get_pull_request_comments` with projectKey='{normalizedProjectKey}', repositorySlug='{normalizedSlug}', pullRequestId={pullRequestId} to see available comments.";
            }
        }

        var taskInfo = new TaskInfo
        {
            Text = text.Trim(),
            Anchor = new TaskBasicAnchor
            {
                Id = commentId,
                Type = "COMMENT"
            }
        };

        var createdTask = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.CreateTaskAsync(taskInfo)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (createdTask is null)
        {
            return "Failed to create task. The Bitbucket API did not return the created task.";
        }

        // Invalidate task-related caches if context parameters were provided
        if (normalizedProjectKey is not null && normalizedSlug is not null && pullRequestId.HasValue)
        {
            ResilientApi.InvalidateCache(CacheKeys.PullRequestTasks(normalizedProjectKey, normalizedSlug, pullRequestId.Value));
            // Invalidate all context variations (16 combinations)
            ResilientApi.InvalidateAllContextVariations(normalizedProjectKey, normalizedSlug, pullRequestId.Value);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Successfully created task");
        sb.AppendLine(new string('-', 50));
        sb.AppendLine($"Task ID: #{createdTask.Id}");
        sb.AppendLine($"State: {createdTask.State}");
        sb.AppendLine($"Anchored to: Comment #{commentId}");

        // Include context info when available
        if (normalizedProjectKey is not null && normalizedSlug is not null && pullRequestId.HasValue)
        {
            sb.AppendLine($"Pull Request: {normalizedProjectKey}/{normalizedSlug} PR #{pullRequestId}");
        }

        sb.AppendLine($"Created: {createdTask.CreatedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Now"}");
        sb.AppendLine();
        sb.AppendLine("Description:");
        sb.AppendLine($"> {createdTask.Text}");

        return sb.ToString();
    }

    [McpServerTool(Name = "update_pull_request_task"), Description("Update the text of an existing task. Note: State changes (open/resolved) are not currently supported by the Bitbucket API - tasks are resolved by addressing the underlying comment.")]
    public async Task<string> UpdatePullRequestTaskAsync(
        [Description("The ID of the task to update.")] int taskId,
        [Description("The new task description text.")] string text,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Task text is required.", nameof(text));
        }

        LogToolInvocation(nameof(UpdatePullRequestTaskAsync),
            (nameof(taskId), taskId),
            (nameof(text), text));

        cancellationToken.ThrowIfCancellationRequested();

        var updatedTask = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.UpdateTaskAsync(taskId, text.Trim())
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (updatedTask is null)
        {
            return $"Failed to update task #{taskId}. The task may not exist.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Successfully updated task #{taskId}");
        sb.AppendLine(new string('-', 50));
        sb.AppendLine($"State: {updatedTask.State}");
        sb.AppendLine();
        sb.AppendLine("New Description:");
        sb.AppendLine($"> {updatedTask.Text}");

        return sb.ToString();
    }

    [McpServerTool(Name = "delete_pull_request_task"), Description("Delete a task from a pull request.")]
    public async Task<string> DeletePullRequestTaskAsync(
        [Description("The ID of the task to delete.")] int taskId,
        CancellationToken cancellationToken = default)
    {
        if (CheckReadOnlyMode() is { } readOnlyError)
        {
            return readOnlyError;
        }

        LogToolInvocation(nameof(DeletePullRequestTaskAsync),
            (nameof(taskId), taskId));

        cancellationToken.ThrowIfCancellationRequested();

        var success = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.DeleteTaskAsync(taskId)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return success
            ? $"Successfully deleted task #{taskId}."
            : $"Failed to delete task #{taskId}. The task may not exist or you may not have permission.";
    }

    #endregion

    #region Private Helpers — Context

    /// <summary>
    /// Safely fetches PR activities with error handling for graceful degradation.
    /// </summary>
    private async Task<(List<PullRequestActivity> Activities, string? Error)> GetActivitiesSafeAsync(
        string projectKey, string repoSlug, int prId, CancellationToken cancellationToken)
    {
        try
        {
            var cacheKey = CacheKeys.PullRequestActivities(projectKey, repoSlug, prId);
            var paginatedActivities = await ResilientApi.ExecuteAsync(
                cacheKey,
                async _ => await Client.GetPullRequestActivitiesStreamAsync(
                    projectKey, repoSlug, (long)prId,
                    cancellationToken: cancellationToken)
                    .TakeWithPaginationAsync(500, cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return (paginatedActivities.Items.ToList(), null);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch activities for PR {PrId} in {ProjectKey}/{RepoSlug}",
                prId, projectKey, repoSlug);
            return ([], ex.Message);
        }
    }

    /// <summary>
    /// Safely fetches PR diff using streaming and formats it, with error handling for graceful degradation.
    /// </summary>
    private async Task<(string? FormattedDiff, string? Error)> GetDiffSafeAsync(
        string projectKey, string repoSlug, int prId, CancellationToken cancellationToken)
    {
        try
        {
            var diffStream = Client.GetPullRequestDiffStreamAsync(
                projectKey, repoSlug, prId, cancellationToken: cancellationToken);

            var formatted = await diffFormatter.FormatDiffStreamAsync(
                diffStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            return (formatted, null);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch diff for PR {PrId} in {ProjectKey}/{RepoSlug}",
                prId, projectKey, repoSlug);
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Safely fetches PR tasks with error handling for graceful degradation.
    /// Tries the new blocker comments API (Bitbucket 9.0+) first, then falls back to legacy tasks.
    /// </summary>
    private async Task<(List<BitbucketTask> Tasks, string? Error)> GetTasksSafeAsync(
        string projectKey, string repoSlug, int prId, CancellationToken cancellationToken)
    {
        // Try blocker comments API first (Bitbucket Server 9.0+)
        try
        {
            var cacheKey = CacheKeys.PullRequestBlockerComments(projectKey, repoSlug, prId);
            var blockerComments = await ResilientApi.ExecuteAsync(
                cacheKey,
                async _ => await Client.GetPullRequestBlockerCommentsAsync(projectKey, repoSlug, prId, limit: 500)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Convert BlockerComment to BitbucketTask for backward compatibility
            // Note: Anchor is not mapped as the types are incompatible (CommentAnchor vs TaskAnchor)
            // and the formatting code doesn't use Anchor anyway
            var tasks = blockerComments.Select(bc => new BitbucketTask
            {
                Id = bc.Id,
                Text = bc.Text,
                State = bc.State == BlockerCommentState.Open ? "OPEN" : "RESOLVED",
                Author = bc.Author,
                CreatedDate = bc.CreatedDate
            }).ToList();

            Logger.LogDebug("Retrieved {Count} blocker comments (tasks) for PR {PrId} using BB 9.0+ API",
                tasks.Count, prId);
            return (tasks, null);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Flurl.Http.FlurlHttpException ex) when (ex.StatusCode == 404)
        {
            // Blocker comments API not available, try legacy tasks API
            Logger.LogDebug("Blocker comments API returned 404 for PR {PrId}, trying legacy tasks API", prId);
        }
        catch (Exception ex)
        {
            // Log and try legacy API
            Logger.LogDebug(ex, "Blocker comments API failed for PR {PrId}, trying legacy tasks API", prId);
        }

        // Fall back to legacy tasks API (Bitbucket Server < 9.0)
        try
        {
            var cacheKey = CacheKeys.PullRequestTasks(projectKey, repoSlug, prId);
#pragma warning disable CS0618 // Type or member is obsolete - intentional fallback for older Bitbucket versions
            var tasks = await ResilientApi.ExecuteAsync(
                cacheKey,
                async _ => await Client.GetPullRequestTasksAsync(projectKey, repoSlug, prId, limit: 500)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore CS0618

            Logger.LogDebug("Retrieved {Count} tasks for PR {PrId} using legacy tasks API",
                tasks.Count(), prId);
            return (tasks.ToList(), null);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Flurl.Http.FlurlHttpException ex) when (ex.StatusCode == 404)
        {
            Logger.LogWarning("Both blocker comments and legacy tasks APIs returned 404 for PR {PrId} in {ProjectKey}/{RepoSlug}.",
                prId, projectKey, repoSlug);
            return ([], "Tasks API not available on this server");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch tasks for PR {PrId} in {ProjectKey}/{RepoSlug}",
                prId, projectKey, repoSlug);
            return ([], ex.Message);
        }
    }

    private void FormatPullRequestMetadata(StringBuilder sb, PullRequest pr)
    {
        sb.AppendLine($"# Pull Request #{pr.Id}: {pr.Title}");
        sb.AppendLine();
        sb.AppendLine($"**State:** {pr.State}");

        var isAuthor = CacheService.IsCurrentUser(pr.Author?.User?.Slug);
        var authorMarker = isAuthor ? " (you)" : string.Empty;
        sb.AppendLine($"**Author:** {pr.Author?.User?.DisplayName ?? "Unknown"}{authorMarker}");

        sb.AppendLine($"**Created:** {pr.CreatedDate?.ToString("u") ?? "Unknown"}");
        sb.AppendLine($"**Updated:** {pr.UpdatedDate?.ToString("u") ?? "Unknown"}");
        sb.AppendLine();
        sb.AppendLine($"**Source:** `{ToolHelpers.FormatBranchRef(pr.FromRef?.Id)}` (Repo: {pr.FromRef?.Repository?.Slug})");
        sb.AppendLine($"**Target:** `{ToolHelpers.FormatBranchRef(pr.ToRef?.Id)}` (Repo: {pr.ToRef?.Repository?.Slug})");

        var link = pr.Links?.Self?.FirstOrDefault()?.Href;
        if (!string.IsNullOrWhiteSpace(link))
        {
            sb.AppendLine($"**Link:** {link}");
        }

        sb.AppendLine();
        sb.AppendLine("## Description");
        sb.AppendLine(string.IsNullOrWhiteSpace(pr.Description) ? "(No description provided)" : pr.Description);
    }

    private void FormatReviewersSection(StringBuilder sb, IEnumerable<Reviewer> reviewers)
    {
        sb.AppendLine("## Reviewers");

        foreach (var reviewer in reviewers)
        {
            var isYou = CacheService.IsCurrentUser(reviewer.User?.Slug);
            var youMarker = isYou ? " (you)" : string.Empty;
            var statusIcon = reviewer.Approved ? "✅" : reviewer.Status == ParticipantStatus.NeedsWork ? "🔧" : "⏳";
            sb.AppendLine($"- {statusIcon} {reviewer.User?.DisplayName ?? "Unknown"}: {reviewer.Status}{youMarker}");
        }
    }

    private void FormatTasksSection(StringBuilder sb, List<BitbucketTask> tasks)
    {
        var openTasks = tasks.Where(t => string.Equals(t.State, "OPEN", StringComparison.OrdinalIgnoreCase)).ToList();
        var resolvedTasks = tasks.Where(t => string.Equals(t.State, "RESOLVED", StringComparison.OrdinalIgnoreCase)).ToList();

        sb.AppendLine($"## Tasks ({openTasks.Count} open, {resolvedTasks.Count} resolved)");

        foreach (var task in tasks.OrderBy(t => t.State == "RESOLVED").ThenBy(t => t.CreatedDate))
        {
            var stateIcon = string.Equals(task.State, "OPEN", StringComparison.OrdinalIgnoreCase) ? "🔴" : "✅";
            var author = task.Author?.DisplayName ?? task.Author?.Name ?? "Unknown";
            var isYou = CacheService.IsCurrentUser(task.Author?.Slug);
            var youMarker = isYou ? " (you)" : string.Empty;

            sb.AppendLine($"- {stateIcon} **Task #{task.Id}** by {author}{youMarker}: {task.Text ?? "(no description)"}");
        }
    }

    private static void FormatActivityTimeline(StringBuilder sb, List<PullRequestActivity> activities)
    {
        sb.AppendLine("## Activity Timeline");

        var nonCommentActivities = activities
            .Where(a => a.Comment is null)
            .OrderByDescending(a => a.CreatedDate)
            .Take(20) // Limit to recent activity
            .ToList();

        if (nonCommentActivities.Count == 0)
        {
            sb.AppendLine("No recent activity.");
            return;
        }

        foreach (var activity in nonCommentActivities)
        {
            var timestamp = activity.CreatedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var user = activity.User?.DisplayName ?? "Unknown";
            var action = activity.Action ?? "Updated";

            sb.AppendLine($"- [{timestamp}] {user}: {action}");
        }
    }

    private void FormatCommentsSection(StringBuilder sb, List<PullRequestActivity> commentActivities)
    {
        sb.AppendLine("## Comments");
        sb.AppendLine($"Total: {commentActivities.Count} comment thread(s)");
        sb.AppendLine();

        foreach (var activity in commentActivities.Take(50)) // Limit to avoid token explosion
        {
            var comment = activity.Comment!;
            var anchor = activity.CommentAnchor;

            sb.AppendLine($"### Comment #{comment.Id}");
            sb.AppendLine($"**Author:** {comment.Author?.DisplayName ?? "Unknown"}");
            sb.AppendLine($"**Date:** {comment.CreatedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown"}");

            if (anchor is not null && !string.IsNullOrWhiteSpace(anchor.Path))
            {
                sb.AppendLine($"**File:** `{anchor.Path}`");
                if (anchor.Line.HasValue)
                {
                    sb.AppendLine($"**Line:** {anchor.Line}");
                }
            }

            sb.AppendLine();
            sb.AppendLine(comment.Text ?? "(empty)");

            // Show replies count
            if (comment.Comments is { Count: > 0 })
            {
                sb.AppendLine($"*({comment.Comments.Count} replies)*");
            }

            sb.AppendLine();
        }
    }

    #endregion

    #region Private Helpers — Comments

    private void FormatComment(
        StringBuilder sb,
        Comment comment,
        CommentAnchor? anchor,
        string? codeContext,
        int indent,
        bool showOnlyUnresolved = false,
        HashSet<int>? resolvedCommentIds = null)
    {
        var indentStr = new string(' ', indent * 2);

        sb.AppendLine($"{indentStr}┌─ Comment #{comment.Id}");
        sb.AppendLine($"{indentStr}│ Author: {comment.Author?.DisplayName ?? comment.Author?.Name ?? "Unknown"}");
        sb.AppendLine($"{indentStr}│ Date: {comment.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown"}");

        if (comment.UpdatedDate.HasValue && comment.UpdatedDate != comment.CreatedDate)
        {
            sb.AppendLine($"{indentStr}│ Updated: {comment.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss")}");
        }

        if (anchor is not null && !string.IsNullOrWhiteSpace(anchor.Path))
        {
            sb.AppendLine($"{indentStr}│ File: {anchor.Path}");
            if (anchor.Line.HasValue)
            {
                sb.AppendLine($"{indentStr}│ Line: {anchor.Line} ({anchor.LineType})");
            }
            if (!string.IsNullOrWhiteSpace(codeContext))
            {
                sb.AppendLine($"{indentStr}│");
                sb.AppendLine($"{indentStr}│ Code context:");
                foreach (var line in codeContext.Split('\n'))
                {
                    sb.AppendLine($"{indentStr}│   {line}");
                }
            }
        }

        sb.AppendLine($"{indentStr}│");
        sb.AppendLine($"{indentStr}│ Comment:");
        foreach (var line in (comment.Text ?? "(empty)").Split('\n'))
        {
            sb.AppendLine($"{indentStr}│   {line}");
        }

        if (comment.Comments is { Count: > 0 })
        {
            var replies = comment.Comments;
            if (showOnlyUnresolved && resolvedCommentIds is not null)
            {
                replies = replies
                    .Where(r => !IsCommentResolved(r, resolvedCommentIds))
                    .ToList();
            }

            if (replies.Count > 0)
            {
                sb.AppendLine($"{indentStr}│");
                sb.AppendLine($"{indentStr}│ Replies ({replies.Count}):");

                foreach (var reply in replies)
                {
                    FormatReply(sb, reply, indent + 1, showOnlyUnresolved, resolvedCommentIds);
                }
            }
        }

        sb.AppendLine($"{indentStr}└─────────────────────────────────");
        sb.AppendLine();
    }

    private static void FormatReply(
        StringBuilder sb,
        Comment reply,
        int indent,
        bool showOnlyUnresolved = false,
        HashSet<int>? resolvedCommentIds = null)
    {
        var indentStr = new string(' ', indent * 2);

        sb.AppendLine($"{indentStr}├── Reply #{reply.Id}");
        sb.AppendLine($"{indentStr}│   Author: {reply.Author?.DisplayName ?? reply.Author?.Name ?? "Unknown"}");
        sb.AppendLine($"{indentStr}│   Date: {reply.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown"}");
        sb.AppendLine($"{indentStr}│");

        foreach (var line in (reply.Text ?? "(empty)").Split('\n'))
        {
            sb.AppendLine($"{indentStr}│   {line}");
        }

        if (reply.Comments is { Count: > 0 })
        {
            var nestedReplies = reply.Comments;
            if (showOnlyUnresolved && resolvedCommentIds is not null)
            {
                nestedReplies = nestedReplies
                    .Where(r => !IsCommentResolved(r, resolvedCommentIds))
                    .ToList();
            }

            foreach (var nestedReply in nestedReplies)
            {
                FormatReply(sb, nestedReply, indent + 1, showOnlyUnresolved, resolvedCommentIds);
            }
        }
    }

    private static AnchorStates ParseAnchorState(string anchorState)
    {
        return anchorState?.ToUpperInvariant() switch
        {
            "ACTIVE" => AnchorStates.Active,
            "ORPHANED" => AnchorStates.Orphaned,
            "ALL" => AnchorStates.All,
            _ => AnchorStates.All
        };
    }

    private static bool MatchesAnchorState(CommentAnchor? anchor, AnchorStates filter)
    {
        if (filter == AnchorStates.All)
        {
            return true;
        }

        if (anchor is null)
        {
            return filter == AnchorStates.Active;
        }

        return true;
    }

    private static bool IsActiveAnchor(CommentAnchor anchor)
    {
        return !string.IsNullOrWhiteSpace(anchor.Path);
    }

    private static bool IsCommentResolved(Comment comment, HashSet<int> resolvedCommentIds)
    {
        // Prefer Bitbucket's native thread resolution/state when available.
        // This matches the Bitbucket UI behavior for resolved discussions.
        if (comment.ThreadResolved == true)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(comment.State) &&
            string.Equals(comment.State, "RESOLVED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fallback to legacy task-based mapping (older Bitbucket versions / endpoints).
        return resolvedCommentIds.Contains(comment.Id);
    }

    private async Task<string?> GetCodeContextForAnchorAsync(
        string projectKey,
        string repoSlug,
        CommentAnchor anchor,
        Dictionary<CodeContextCacheKey, string?> cache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(anchor.Path) || !anchor.Line.HasValue)
        {
            return null;
        }

        var isFromSide = anchor.FileType == FileTypes.From || anchor.LineType == LineTypes.Removed;
        var at = isFromSide ? anchor.FromHash : anchor.ToHash;
        var path = isFromSide && !string.IsNullOrWhiteSpace(anchor.SrcPath)
            ? anchor.SrcPath
            : anchor.Path;

        if (string.IsNullOrWhiteSpace(at) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var key = new CodeContextCacheKey(at, path, anchor.Line.Value);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var context = await ExtractCodeContextFromRawFileAsync(projectKey, repoSlug, path, at, anchor.Line.Value, cancellationToken)
            .ConfigureAwait(false);

        cache[key] = context;
        return context;
    }

    private async Task<string?> ExtractCodeContextFromRawFileAsync(
        string projectKey,
        string repoSlug,
        string path,
        string at,
        int targetLine,
        CancellationToken cancellationToken)
    {
        const int contextRadius = 2;

        if (targetLine <= 0)
        {
            return null;
        }

        var startLine = Math.Max(1, targetLine - contextRadius);
        var endLine = targetLine + contextRadius;

        try
        {
            await using var stream = await Client.GetRawFileContentStreamAsync(projectKey, repoSlug, path, at, cancellationToken)
                .ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sb = new StringBuilder();

            var currentLine = 0;
            while (currentLine < endLine)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                currentLine++;
                if (currentLine < startLine)
                {
                    continue;
                }

                var marker = currentLine == targetLine ? " <-- HERE" : string.Empty;
                sb.AppendLine($"{currentLine,4}: {line}{marker}");
            }

            var result = sb.ToString().TrimEnd();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex,
                "Failed to load code context from raw file for {ProjectKey}/{RepoSlug} at {At} path {Path} line {Line}",
                projectKey,
                repoSlug,
                at,
                path,
                targetLine);
            return null;
        }
    }

    // Note: We intentionally avoid using the PR diff endpoint for code context.
    // Some Bitbucket instances return text/plain diffs, which causes JSON deserialization warnings
    // across many tool calls. Raw file snippets provide reliable context without requiring diff parsing.

    #endregion

    #region Private Helpers — Management

    private static string FormatPullRequestSummary(PullRequest? pullRequest)
    {
        if (pullRequest is null)
        {
            return "Operation completed, but the Bitbucket API did not return pull request details.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Pull Request #{pullRequest.Id}: {pullRequest.Title}");
        sb.AppendLine($"State: {pullRequest.State}");
        sb.AppendLine($"Source: {pullRequest.FromRef?.Id ?? "(unknown)"}");
        sb.AppendLine($"Target: {pullRequest.ToRef?.Id ?? "(unknown)"}");
        sb.AppendLine($"Updated: {pullRequest.UpdatedDate?.ToString("u") ?? "(unknown)"}");

        var reviewerNames = pullRequest.Reviewers?
            .Select(r => r.User?.DisplayName ?? r.User?.Name ?? r.User?.Slug)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (reviewerNames is { Count: > 0 })
        {
            sb.AppendLine($"Reviewers: {string.Join(", ", reviewerNames)}");
        }

        var link = pullRequest.Links?.Self?.FirstOrDefault()?.Href;
        if (!string.IsNullOrWhiteSpace(link))
        {
            sb.AppendLine($"Link: {link}");
        }

        return sb.ToString();
    }

    private void FormatTask(StringBuilder sb, Bitbucket.Net.Models.Core.Tasks.BitbucketTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var stateIcon = task.State?.ToUpperInvariant() == "OPEN" ? "🔴" : "✅";
        var author = task.Author?.DisplayName ?? task.Author?.Name ?? "Unknown";
        var isYou = CacheService.IsCurrentUser(task.Author?.Slug);
        var youMarker = isYou ? " (you)" : string.Empty;

        sb.AppendLine($"### {stateIcon} Task #{task.Id}");
        sb.AppendLine($"**State:** {task.State}");
        sb.AppendLine($"**Author:** {author}{youMarker}");
        sb.AppendLine($"**Created:** {task.CreatedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown"}");
        sb.AppendLine();
        sb.AppendLine("**Description:**");
        sb.AppendLine($"> {task.Text ?? "(no description)"}");

        // Show anchor info if available
        if (task.Anchor is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"*Anchored to comment #{task.Anchor.Id}*");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    #endregion

    #region Inner Types

    private readonly record struct CodeContextCacheKey(string At, string Path, int Line);

    #endregion
}