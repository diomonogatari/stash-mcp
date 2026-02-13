using Bitbucket.Net;
using Bitbucket.Net.Models.Core.Projects;
using ModelContextProtocol.Server;
using StashMcpServer.Formatting;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class SearchTools(
    ILogger<SearchTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    IBitbucketClient client,
    IServerSettings serverSettings)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    private const int DefaultSearchLimit = 30;
    private const int MaxSearchLimit = 100;
    private const int ContextLines = 2;
    private const int MaxFileSizeForSearch = 512 * 1024; // 512KB - skip larger files for performance
    private const int MaxFilesToScan = 1000; // Limit files scanned per search
    private const int SearchParallelism = 4; // Max concurrent file searches
    private const int DefaultCommitSearchLimit = 50;
    private const int MaxCommitSearchLimit = 500;
    private const int DefaultPrSearchLimit = 25;
    private const int MaxPrSearchLimit = 100;
    private const int DefaultUserSearchLimit = 25;
    private const int MaxUserSearchLimit = 100;

    #region Code Search

    [McpServerTool(Name = "search_code"), Description("""
        Search for code patterns in repository files.
        
        Uses server-side Elasticsearch when available for fast, indexed search.
        Falls back to grep-style file scanning when server search is unavailable
        or when advanced options (regex, case-sensitive) are used.
        
        Returns file paths with matching line numbers and context snippets.
        """)]
    public async Task<string> SearchCodeAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The search query - supports plain text or regex pattern.")] string query,
        [Description("Optional: glob pattern to filter files (e.g., '*.cs', 'src/**/*.ts'). Defaults to all files.")] string? pathPattern = null,
        [Description("Optional: branch or commit to search. Defaults to repository's default branch.")] string? at = null,
        [Description("Use regex matching instead of literal text search. Default: false")] bool isRegex = false,
        [Description("Case-sensitive search. Default: false")] bool caseSensitive = false,
        [Description("Maximum number of results to return. Default: 30, Max: 100")] int limit = DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: Search query is required.";
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        var effectiveRef = at;
        if (string.IsNullOrWhiteSpace(effectiveRef))
        {
            effectiveRef = await CacheService.GetDefaultBranchNameAsync(normalizedProjectKey, normalizedSlug, cancellationToken).ConfigureAwait(false) ?? "master";
        }

        var effectiveLimit = Math.Clamp(limit, 1, MaxSearchLimit);

        LogToolInvocation(nameof(SearchCodeAsync),
            (nameof(projectKey), normalizedProjectKey),
            (nameof(repositorySlug), normalizedSlug),
            (nameof(query), query),
            (nameof(pathPattern), pathPattern),
            (nameof(at), effectiveRef),
            (nameof(isRegex), isRegex),
            (nameof(caseSensitive), caseSensitive),
            (nameof(limit), effectiveLimit));

        // Server-side search is preferred when:
        // 1. Search is available on the server
        // 2. No regex or case-sensitive flags (server doesn't support those)
        // 3. No specific branch/commit override (server searches indexed default branch)
        var useServerSearch = CacheService.IsSearchAvailable()
            && !isRegex
            && !caseSensitive
            && string.IsNullOrWhiteSpace(at);

        if (useServerSearch)
        {
            try
            {
                var result = await SearchCodeServerSideAsync(
                    normalizedProjectKey, normalizedSlug, query,
                    pathPattern, effectiveLimit, cancellationToken)
                    .ConfigureAwait(false);

                if (result is not null)
                {
                    return result;
                }

                // Null result means server search returned no usable data; fall through to grep
                Logger.LogDebug("Server-side search returned no results, falling back to grep for {Project}/{Repo}",
                    normalizedProjectKey, normalizedSlug);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Server-side search failed for {Project}/{Repo}, falling back to grep",
                    normalizedProjectKey, normalizedSlug);
            }
        }

        return await SearchCodeClientSideAsync(
            normalizedProjectKey, normalizedSlug, query, pathPattern,
            effectiveRef, isRegex, caseSensitive, effectiveLimit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a server-side Elasticsearch-backed code search.
    /// Returns null if the search produces no usable results (triggering fallback).
    /// </summary>
    private async Task<string?> SearchCodeServerSideAsync(
        string projectKey,
        string repositorySlug,
        string query,
        string? pathPattern,
        int limit,
        CancellationToken cancellationToken)
    {
        // Build scoped query: project:KEY repo:slug [ext:ext] [path:dir] <query>
        var scopedQuery = new StringBuilder();
        scopedQuery.Append($"project:{projectKey} repo:{repositorySlug}");

        // Convert pathPattern to server search syntax
        if (!string.IsNullOrWhiteSpace(pathPattern))
        {
            // Extract extension from simple glob patterns like "*.cs"
            if (pathPattern.StartsWith("*.") && !pathPattern.Contains('/'))
            {
                var ext = pathPattern[2..]; // strip "*."
                scopedQuery.Append($" ext:{ext}");
            }
            // Extract path prefix from patterns like "src/**" or "src/"
            else if (pathPattern.Contains('/'))
            {
                var pathPrefix = pathPattern
                    .Replace("**", "")
                    .Replace("*", "")
                    .TrimEnd('/');

                if (!string.IsNullOrWhiteSpace(pathPrefix))
                {
                    scopedQuery.Append($" path:{pathPrefix}");
                }
            }
        }

        scopedQuery.Append($" {query}");

        Logger.LogDebug("Server-side code search query: {Query}", scopedQuery);

        cancellationToken.ThrowIfCancellationRequested();

        var response = await ResilientApi.ExecuteWithoutCacheAsync(
            async _ => await Client.SearchCodeAsync(
                scopedQuery.ToString(),
                primaryLimit: limit,
                secondaryLimit: 10,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var codeResults = response?.Code?.Values;
        if (codeResults is null || codeResults.Count == 0)
        {
            return null;
        }

        // Convert server results to SearchMatch objects for consistent formatting
        var matches = new List<SearchMatch>();

        foreach (var result in codeResults)
        {
            if (result.File is null || result.HitContexts is null)
            {
                continue;
            }

            foreach (var contextBlock in result.HitContexts)
            {
                foreach (var hitLine in contextBlock)
                {
                    if (matches.Count >= limit)
                    {
                        break;
                    }

                    // Strip <em> highlighting tags from the text
                    var cleanText = StripHighlightTags(hitLine.Text ?? "");

                    // Build context from surrounding lines in the same block
                    var lineIndex = contextBlock.IndexOf(hitLine);
                    var contextBefore = contextBlock
                        .Take(lineIndex)
                        .TakeLast(ContextLines)
                        .Select(l => StripHighlightTags(l.Text ?? ""))
                        .ToArray();
                    var contextAfter = contextBlock
                        .Skip(lineIndex + 1)
                        .Take(ContextLines)
                        .Select(l => StripHighlightTags(l.Text ?? ""))
                        .ToArray();

                    // Only include lines that contain highlighted matches
                    if (hitLine.Text?.Contains("<em>") is true)
                    {
                        matches.Add(new SearchMatch
                        {
                            FilePath = result.File,
                            LineNumber = hitLine.Line,
                            MatchedLine = cleanText,
                            ContextBefore = contextBefore,
                            ContextAfter = contextAfter
                        });
                    }
                }

                if (matches.Count >= limit)
                {
                    break;
                }
            }

            if (matches.Count >= limit)
            {
                break;
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }

        var totalHits = response.Code!.Count;
        return FormatServerSearchResults(matches, projectKey, repositorySlug, query,
            totalHits, response.Code.IsLastPage, limit);
    }

    /// <summary>
    /// Formats server-side search results.
    /// </summary>
    private static string FormatServerSearchResults(
        List<SearchMatch> results,
        string projectKey,
        string repositorySlug,
        string query,
        int totalHitCount,
        bool isLastPage,
        int limit)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"# Search Results for '{query}'");
        builder.AppendLine($"Repository: {projectKey}/{repositorySlug}");
        builder.AppendLine($"Search: server-side (Elasticsearch)");
        builder.AppendLine($"Total matches: {totalHitCount}");
        builder.AppendLine();

        builder.AppendLine($"**Showing {results.Count} match(es)**");
        if (!isLastPage || results.Count >= limit)
        {
            builder.AppendLine("_More results available. Refine query or increase limit._");
        }

        builder.AppendLine();

        // Group by file
        var groupedResults = results
            .GroupBy(r => r.FilePath)
            .OrderBy(g => g.Key);

        var maxOutputLength = ResponseTruncation.GetAvailableLength(ResponseTruncation.SearchTruncationHint);

        foreach (var fileGroup in groupedResults)
        {
            if (builder.Length > maxOutputLength)
            {
                break;
            }

            builder.AppendLine($"## {fileGroup.Key}");
            builder.AppendLine();

            foreach (var match in fileGroup.OrderBy(m => m.LineNumber))
            {
                builder.AppendLine($"**Line {match.LineNumber}:**");
                builder.AppendLine("```");

                foreach (var ctx in match.ContextBefore)
                {
                    builder.AppendLine($"  {ctx}");
                }

                builder.AppendLine($"> {match.MatchedLine}");

                foreach (var ctx in match.ContextAfter)
                {
                    builder.AppendLine($"  {ctx}");
                }

                builder.AppendLine("```");
                builder.AppendLine();
            }
        }

        return ResponseTruncation.TruncateSearchResults(builder.ToString());
    }

    /// <summary>
    /// Strips Bitbucket search highlight &lt;em&gt; tags from text.
    /// </summary>
    private static string StripHighlightTags(string text)
    {
        return text
            .Replace("<em>", "", StringComparison.OrdinalIgnoreCase)
            .Replace("</em>", "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs a client-side grep-style search through repository files.
    /// Used as fallback when server-side search is unavailable or unsupported.
    /// </summary>
    private async Task<string> SearchCodeClientSideAsync(
        string normalizedProjectKey,
        string normalizedSlug,
        string query,
        string? pathPattern,
        string effectiveRef,
        bool isRegex,
        bool caseSensitive,
        int effectiveLimit,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build the search regex
            var searchPattern = BuildSearchPattern(query, isRegex, caseSensitive);

            cancellationToken.ThrowIfCancellationRequested();

            // Get file list with resilience (no caching since file lists can change)
            var allFiles = await ResilientApi.ExecuteWithoutCacheAsync(
                async _ => await Client
                    .GetRepositoryFilesAsync(normalizedProjectKey, normalizedSlug, effectiveRef, limit: MaxFilesToScan)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var files = allFiles.ToList();
            var filteredFiles = FilterFiles(files, pathPattern);

            Logger.LogDebug("Searching {FileCount} files (filtered from {TotalFiles}) in {Project}/{Repo}@{Ref}",
                filteredFiles.Count, files.Count, normalizedProjectKey, normalizedSlug, effectiveRef);

            // Filter out binary files upfront
            var searchableFiles = filteredFiles
                .Where(f => !ToolHelpers.IsLikelyBinary(f))
                .ToList();

            var filesSkipped = filteredFiles.Count - searchableFiles.Count;

            // Use parallel processing with controlled concurrency
            var semaphore = new SemaphoreSlim(SearchParallelism);
            var searchTasks = searchableFiles.Select(async filePath =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var matches = await SearchFileAsync(
                        normalizedProjectKey,
                        normalizedSlug,
                        filePath,
                        effectiveRef,
                        searchPattern,
                        effectiveLimit,
                        cancellationToken).ConfigureAwait(false);

                    return (FilePath: filePath, Matches: matches, Success: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.LogDebug(ex, "Skipping file {FilePath}", filePath);
                    return (FilePath: filePath, Matches: new List<SearchMatch>(), Success: false);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var taskResults = await Task.WhenAll(searchTasks).ConfigureAwait(false);

            // Aggregate results respecting the limit
            var results = new List<SearchMatch>();
            var filesScanned = 0;

            foreach (var taskResult in taskResults)
            {
                if (!taskResult.Success)
                {
                    filesSkipped++;
                    continue;
                }

                filesScanned++;

                foreach (var match in taskResult.Matches)
                {
                    if (results.Count >= effectiveLimit)
                    {
                        break;
                    }

                    results.Add(match);
                }

                if (results.Count >= effectiveLimit)
                {
                    break;
                }
            }

            return FormatSearchResults(results, normalizedProjectKey, normalizedSlug, effectiveRef, query, filesScanned, filesSkipped, effectiveLimit);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Search failed for {Project}/{Repo}", normalizedProjectKey, normalizedSlug);
            return $"Error: Search failed - {ex.Message}";
        }
    }

    #endregion

    #region Commit Search

    [McpServerTool(Name = "search_commits"), Description("""
        Search commit messages across a repository or multiple repositories.
        
        Searches commit history by message text, author, and/or date range.
        Returns commits matching the search criteria with metadata.
        
        When no fromRef is specified, searches across ALL branches in the repository.
        When fromRef is specified, only searches commits reachable from that ref.
        
        For cross-repository search, use projectKey without repositorySlug to search
        all repositories in a project (note: may be slow for large projects).
        """)]
    public async Task<string> SearchCommitsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository. If empty, searches all repositories in the project.")] string? repositorySlug = null,
        [Description("Search text to match in commit messages (case-insensitive).")] string? messageContains = null,
        [Description("Filter by author name or email (case-insensitive partial match).")] string? author = null,
        [Description("Branch or commit to search from. When omitted, searches all branches.")] string? fromRef = null,
        [Description("Only include commits after this date (ISO 8601 format, e.g., '2024-01-15').")] string? since = null,
        [Description("Only include commits before this date (ISO 8601 format, e.g., '2024-12-31').")] string? until = null,
        [Description("Maximum number of commits to return. Default: 50, Max: 500")] int limit = DefaultCommitSearchLimit,
        CancellationToken cancellationToken = default)
    {
        // Validate we have at least one search criteria
        if (string.IsNullOrWhiteSpace(messageContains) &&
            string.IsNullOrWhiteSpace(author) &&
            string.IsNullOrWhiteSpace(since) &&
            string.IsNullOrWhiteSpace(until))
        {
            return "Error: At least one search criteria is required (messageContains, author, since, or until).";
        }

        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var effectiveLimit = Math.Clamp(limit, 1, MaxCommitSearchLimit);

        LogToolInvocation(nameof(SearchCommitsAsync),
            (nameof(projectKey), normalizedProjectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(messageContains), messageContains),
            (nameof(author), author),
            (nameof(fromRef), fromRef),
            (nameof(since), since),
            (nameof(until), until),
            (nameof(limit), effectiveLimit));

        cancellationToken.ThrowIfCancellationRequested();

        // Parse date filters if provided
        DateTimeOffset? sinceDate = null;
        DateTimeOffset? untilDate = null;

        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedSince))
            {
                return $"Error: Invalid 'since' date format: '{since}'. Use ISO 8601 format (e.g., '2024-01-15').";
            }
            sinceDate = parsedSince;
        }

        if (!string.IsNullOrWhiteSpace(until))
        {
            if (!DateTimeOffset.TryParse(until, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedUntil))
            {
                return $"Error: Invalid 'until' date format: '{until}'. Use ISO 8601 format (e.g., '2024-12-31').";
            }
            untilDate = parsedUntil;
        }

        try
        {
            var results = new List<CommitSearchResult>();

            if (!string.IsNullOrWhiteSpace(repositorySlug))
            {
                // Search single repository
                var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
                var repoResults = await SearchCommitsInRepositoryAsync(
                    normalizedProjectKey, normalizedSlug, messageContains, author,
                    fromRef, sinceDate, untilDate, effectiveLimit, cancellationToken)
                    .ConfigureAwait(false);
                results.AddRange(repoResults);
            }
            else
            {
                // Search all repositories in project
                var repositories = CacheService.GetRepositories(normalizedProjectKey).ToList();

                if (repositories.Count == 0)
                {
                    return $"No repositories found in project '{normalizedProjectKey}'.";
                }

                // Search repositories with limited parallelism
                var semaphore = new SemaphoreSlim(3);
                var searchTasks = repositories.Select(async repo =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return await SearchCommitsInRepositoryAsync(
                            normalizedProjectKey, repo.Slug!, messageContains, author,
                            fromRef, sinceDate, untilDate, effectiveLimit / repositories.Count + 1, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Logger.LogWarning(ex, "Commit search failed for {Repo}, skipping", repo.Slug);
                        return new List<CommitSearchResult>();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var allResults = await Task.WhenAll(searchTasks).ConfigureAwait(false);
                results = allResults.SelectMany(r => r).ToList();
            }

            // Sort by date descending and apply limit
            results = results
                .OrderByDescending(r => r.Timestamp)
                .Take(effectiveLimit)
                .ToList();

            return FormatCommitSearchResults(results, normalizedProjectKey, repositorySlug,
                messageContains, author, sinceDate, untilDate, effectiveLimit);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Commit search failed for {Project}", normalizedProjectKey);
            return $"Error: Commit search failed - {ex.Message}";
        }
    }

    #endregion

    #region Pull Request Search

    [McpServerTool(Name = "search_pull_requests"), Description("""
        Search pull requests by title, description, author, or state across a repository or project.
        
        Searches PR metadata and returns matching pull requests with key information.
        Supports filtering by state (OPEN, MERGED, DECLINED, ALL) and author.
        
        For cross-repository search, use projectKey without repositorySlug to search
        all repositories in a project.
        """)]
    public async Task<string> SearchPullRequestsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository. If empty, searches all repositories in the project.")] string? repositorySlug = null,
        [Description("Search text to match in PR title or description (case-insensitive).")] string? textContains = null,
        [Description("Filter by author username or display name (case-insensitive partial match).")] string? author = null,
        [Description("Filter by state: 'OPEN', 'MERGED', 'DECLINED', or 'ALL'. Default: 'ALL'")] string state = "ALL",
        [Description("Filter by target branch name (e.g., 'main', 'develop').")] string? targetBranch = null,
        [Description("Filter by source branch name.")] string? sourceBranch = null,
        [Description("Maximum number of results to return. Default: 25, Max: 100")] int limit = DefaultPrSearchLimit,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var effectiveLimit = Math.Clamp(limit, 1, MaxPrSearchLimit);
        var normalizedState = state.ToUpperInvariant();

        // Validate and parse state
        if (!TryParsePullRequestState(normalizedState, out var apiState))
        {
            return $"Error: Invalid state '{state}'. Must be one of: OPEN, MERGED, DECLINED, ALL";
        }

        LogToolInvocation(nameof(SearchPullRequestsAsync),
            (nameof(projectKey), normalizedProjectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(textContains), textContains),
            (nameof(author), author),
            (nameof(state), normalizedState),
            (nameof(targetBranch), targetBranch),
            (nameof(sourceBranch), sourceBranch),
            (nameof(limit), effectiveLimit));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var results = new List<PullRequestSearchResult>();

            if (!string.IsNullOrWhiteSpace(repositorySlug))
            {
                // Search single repository
                var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
                var repoResults = await SearchPullRequestsInRepositoryAsync(
                    normalizedProjectKey, normalizedSlug, textContains, author,
                    apiState, targetBranch, sourceBranch, effectiveLimit, cancellationToken)
                    .ConfigureAwait(false);
                results.AddRange(repoResults);
            }
            else
            {
                // Search all repositories in project
                var repositories = CacheService.GetRepositories(normalizedProjectKey).ToList();

                if (repositories.Count == 0)
                {
                    return $"No repositories found in project '{normalizedProjectKey}'.";
                }

                // Search repositories with limited parallelism
                var semaphore = new SemaphoreSlim(3);
                var searchTasks = repositories.Select(async repo =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return await SearchPullRequestsInRepositoryAsync(
                            normalizedProjectKey, repo.Slug!, textContains, author,
                            apiState, targetBranch, sourceBranch,
                            effectiveLimit / repositories.Count + 1, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Logger.LogWarning(ex, "PR search failed for {Repo}, skipping", repo.Slug);
                        return new List<PullRequestSearchResult>();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var allResults = await Task.WhenAll(searchTasks).ConfigureAwait(false);
                results = allResults.SelectMany(r => r).ToList();
            }

            // Sort by updated date descending and apply limit
            results = results
                .OrderByDescending(r => r.UpdatedDate)
                .Take(effectiveLimit)
                .ToList();

            return FormatPullRequestSearchResults(results, normalizedProjectKey, repositorySlug,
                textContains, author, normalizedState, targetBranch, sourceBranch, effectiveLimit);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "PR search failed for {Project}", normalizedProjectKey);
            return $"Error: Pull request search failed - {ex.Message}";
        }
    }

    #endregion

    #region User Search

    [McpServerTool(Name = "search_users"), Description("""
        Search for Bitbucket users by name, username, or email.
        
        This tool helps find users for operations like adding reviewers to pull requests
        or mentioning users in comments. Returns user slugs that can be used with other tools.
        
        Note: Access to user search may require appropriate permissions depending on 
        Bitbucket Server configuration.
        """)]
    public async Task<string> SearchUsersAsync(
        [Description("Search query (matches name, username, or email). Case-insensitive fuzzy match.")] string query,
        [Description("Maximum number of results to return. Default: 25, Max: 100")] int limit = DefaultUserSearchLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "❌ Search query is required. Provide a name, username, or email to search for.";
        }

        var effectiveLimit = Math.Clamp(limit, 1, MaxUserSearchLimit);
        var trimmedQuery = query.Trim();

        LogToolInvocation(nameof(SearchUsersAsync),
            (nameof(query), trimmedQuery),
            (nameof(limit), effectiveLimit));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use the filter parameter to search users
            var cacheKey = CacheKeys.UserSearch(trimmedQuery, effectiveLimit);
            var users = await ResilientApi.ExecuteAsync(
                cacheKey,
                async _ => await Client.GetUsersAsync(
                    filter: trimmedQuery,
                    limit: effectiveLimit,
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var userList = users.ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"# User Search Results for \"{trimmedQuery}\"");
            sb.AppendLine();

            if (userList.Count == 0)
            {
                sb.AppendLine("No users found matching your query.");
                sb.AppendLine();
                sb.AppendLine("**Tips:**");
                sb.AppendLine("- Try a partial name or username");
                sb.AppendLine("- Check spelling of the name or email");
                sb.AppendLine("- The search is case-insensitive");
                return sb.ToString();
            }

            sb.AppendLine($"**Found {userList.Count} user(s)**");
            if (userList.Count >= effectiveLimit)
            {
                sb.AppendLine($"_Results limited to {effectiveLimit}. Try a more specific query for better results._");
            }
            sb.AppendLine();

            // Format results as a table
            sb.AppendLine("| Slug | Display Name | Email | Status |");
            sb.AppendLine("|------|--------------|-------|--------|");

            foreach (var user in userList)
            {
                var status = user.Active ? "✅ Active" : "⚠️ Inactive";
                var displayName = user.DisplayName ?? user.Name ?? "Unknown";
                var email = user.EmailAddress ?? "—";
                var slug = user.Slug ?? user.Name ?? "—";

                // Escape pipe characters in display values
                displayName = displayName.Replace("|", "\\|");
                email = email.Replace("|", "\\|");

                sb.AppendLine($"| `{slug}` | {displayName} | {email} | {status} |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("_Use the `Slug` value when adding reviewers to pull requests or in other user-related operations._");

            return sb.ToString();
        }
        catch (Flurl.Http.FlurlHttpException ex) when (ex.StatusCode == 403)
        {
            Logger.LogWarning(ex, "User search denied - insufficient permissions");
            return """
                ❌ **Permission Denied**

                User search requires appropriate permissions on this Bitbucket Server instance.
                
                This may happen if:
                - User search is restricted to administrators
                - Your access token doesn't have sufficient permissions
                - The server has user visibility restrictions enabled
                
                Please contact your Bitbucket administrator for access.
                """;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "User search failed for query: {Query}", trimmedQuery);
            return $"❌ Failed to search users: {ex.Message}";
        }
    }

    #endregion

    #region Private Helpers — Code Search

    private static string FormatSearchResults(
        List<SearchMatch> results,
        string projectKey,
        string repositorySlug,
        string reference,
        string query,
        int filesScanned,
        int filesSkipped,
        int limit)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"# Search Results for '{query}'");
        builder.AppendLine($"Repository: {projectKey}/{repositorySlug} @ {reference}");
        builder.AppendLine($"Files scanned: {filesScanned}, skipped (binary/large): {filesSkipped}");
        builder.AppendLine();

        if (results.Count == 0)
        {
            builder.AppendLine("No matches found.");
            builder.AppendLine();
            builder.AppendLine("Tips:");
            builder.AppendLine("- Try a different search term or pattern");
            builder.AppendLine("- Use pathPattern to narrow search scope (e.g., '*.cs', 'src/**/*.ts')");
            builder.AppendLine("- Check if the pattern is case-sensitive");
            return builder.ToString();
        }

        builder.AppendLine($"**Found {results.Count} match(es)**");
        if (results.Count >= limit)
        {
            builder.AppendLine($"_Results limited to {limit}. Use pathPattern to narrow search or increase limit._");
        }

        builder.AppendLine();

        // Group by file
        var groupedResults = results
            .GroupBy(r => r.FilePath)
            .OrderBy(g => g.Key);

        // Track output length to avoid excessive output
        var maxOutputLength = ResponseTruncation.GetAvailableLength(ResponseTruncation.SearchTruncationHint);

        foreach (var fileGroup in groupedResults)
        {
            // Check if we're approaching truncation limit
            if (builder.Length > maxOutputLength)
            {
                break;
            }

            builder.AppendLine($"## {fileGroup.Key}");
            builder.AppendLine();

            foreach (var match in fileGroup.OrderBy(m => m.LineNumber))
            {
                builder.AppendLine($"**Line {match.LineNumber}:**");
                builder.AppendLine("```");

                // Context before
                foreach (var ctx in match.ContextBefore)
                {
                    builder.AppendLine($"  {ctx}");
                }

                // Matched line (highlighted)
                builder.AppendLine($"> {match.MatchedLine}");

                // Context after
                foreach (var ctx in match.ContextAfter)
                {
                    builder.AppendLine($"  {ctx}");
                }

                builder.AppendLine("```");
                builder.AppendLine();
            }
        }

        // Apply truncation if needed
        return ResponseTruncation.TruncateSearchResults(builder.ToString());
    }

    private static Regex BuildSearchPattern(string query, bool isRegex, bool caseSensitive)
    {
        var options = RegexOptions.Compiled;
        if (!caseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        var pattern = isRegex ? query : Regex.Escape(query);
        return new Regex(pattern, options, TimeSpan.FromSeconds(5));
    }

    private static List<string> FilterFiles(List<string> files, string? pathPattern)
    {
        if (string.IsNullOrWhiteSpace(pathPattern))
        {
            return files;
        }

        // Convert glob pattern to regex
        var globRegex = GlobToRegex(pathPattern);

        return files
            .Where(f => globRegex.IsMatch(f))
            .ToList();
    }

    /// <summary>
    /// Converts a glob pattern to a regex pattern.
    /// Supported patterns:
    /// - * matches any filename characters (except /)
    /// - ** matches any path (including /)
    /// - ? matches a single character
    /// Examples: "*.cs", "src/**/*.ts", "test?.js"
    /// </summary>
    private static Regex GlobToRegex(string pattern)
    {
        // First escape the pattern, then handle glob tokens in correct order
        var escaped = Regex.Escape(pattern);

        // Use a placeholder for ** to avoid conflicts with single *
        // Order matters: handle ** before * to prevent *** edge cases
        var regexPattern = escaped
            .Replace(@"\*\*", "\x00GLOBSTAR\x00")  // Temporary placeholder for **
            .Replace(@"\*", "[^/]*")               // * matches any filename chars (not /)
            .Replace("\x00GLOBSTAR\x00", ".*")     // ** matches any path including /
            .Replace(@"\?", ".");                  // ? matches single char

        return new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private async Task<List<SearchMatch>> SearchFileAsync(
        string projectKey,
        string repositorySlug,
        string filePath,
        string reference,
        Regex pattern,
        int maxMatches,
        CancellationToken cancellationToken = default)
    {
        var matches = new List<SearchMatch>();

        // Use streaming API to read file line-by-line with early termination support
        // This avoids loading entire large files into memory
        var allLines = new List<string>();
        var lineCount = 0;

        await foreach (var line in Client.GetRawFileContentLinesStreamAsync(
            projectKey, repositorySlug, filePath, reference, cancellationToken).ConfigureAwait(false))
        {
            // Check if file is too large (stop early)
            lineCount++;
            if (lineCount > MaxFileSizeForSearch / 80) // Rough estimate: 80 chars per line avg
            {
                Logger.LogDebug("Skipping large file {FilePath} (>{LineCount} lines)", filePath, lineCount);
                return matches;
            }

            allLines.Add(line);
        }

        // Find matches and populate context
        for (var lineIndex = 0; lineIndex < allLines.Count && matches.Count < maxMatches; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentLine = allLines[lineIndex];

            if (pattern.IsMatch(currentLine))
            {
                var match = new SearchMatch
                {
                    FilePath = filePath,
                    LineNumber = lineIndex + 1, // 1-based line numbers
                    MatchedLine = TruncateLine(currentLine),
                    ContextBefore = GetContextBefore(allLines, lineIndex),
                    ContextAfter = GetContextAfter(allLines, lineIndex)
                };

                matches.Add(match);
            }
        }

        return matches;
    }

    private static string TruncateLine(string line, int maxLength = 200)
    {
        return line.Length > maxLength ? line[..(maxLength - 3)] + "..." : line;
    }

    private static string[] GetContextBefore(List<string> lines, int matchIndex)
    {
        var result = new List<string>();
        var startIndex = Math.Max(0, matchIndex - ContextLines);

        for (var i = startIndex; i < matchIndex; i++)
        {
            result.Add(TruncateLine(lines[i]));
        }

        return result.ToArray();
    }

    private static string[] GetContextAfter(List<string> lines, int matchIndex)
    {
        var result = new List<string>();
        var endIndex = Math.Min(lines.Count, matchIndex + ContextLines + 1);

        for (var i = matchIndex + 1; i < endIndex; i++)
        {
            result.Add(TruncateLine(lines[i]));
        }

        return result.ToArray();
    }

    #endregion

    #region Private Helpers — Commit Search

    private async Task<List<CommitSearchResult>> SearchCommitsInRepositoryAsync(
        string projectKey,
        string repoSlug,
        string? messageContains,
        string? author,
        string? fromRef,
        DateTimeOffset? since,
        DateTimeOffset? until,
        int limit,
        CancellationToken cancellationToken)
    {
        var results = new List<CommitSearchResult>();
        var seenCommitIds = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(fromRef))
        {
            // Explicit branch specified — search only that branch
            await SearchCommitsOnRefAsync(projectKey, repoSlug, fromRef, messageContains, author, since, until, limit, results, seenCommitIds, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // No branch specified — search all branches to find commits on feature branches too
            var branches = new List<string>();
            await foreach (var branch in Client.Branches(projectKey, repoSlug).StreamAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (branch.Id is not null)
                {
                    branches.Add(branch.Id);
                }
            }

            // Search default branch first, then remaining branches
            var defaultRef = await CacheService.GetDefaultBranchRefAsync(projectKey, repoSlug, cancellationToken: cancellationToken).ConfigureAwait(false);
            var orderedBranches = branches
                .OrderByDescending(b => string.Equals(b, defaultRef, StringComparison.Ordinal))
                .ToList();

            foreach (var branchRef in orderedBranches)
            {
                if (results.Count >= limit)
                {
                    break;
                }

                await SearchCommitsOnRefAsync(projectKey, repoSlug, branchRef, messageContains, author, since, until, limit, results, seenCommitIds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return results;
    }

    private async Task SearchCommitsOnRefAsync(
        string projectKey,
        string repoSlug,
        string refName,
        string? messageContains,
        string? author,
        DateTimeOffset? since,
        DateTimeOffset? until,
        int limit,
        List<CommitSearchResult> results,
        HashSet<string> seenCommitIds,
        CancellationToken cancellationToken)
    {
        await foreach (var commit in Client.Commits(projectKey, repoSlug, refName)
            .FollowRenames(false)
            .IgnoreMissing(true)
            .WithCounts(false)
            .StreamAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Deduplicate across branches
            var commitId = commit.Id ?? string.Empty;
            if (!seenCommitIds.Add(commitId))
            {
                continue;
            }

            if (!MatchesCommitFilters(commit, messageContains, author, since, until))
            {
                continue;
            }

            results.Add(new CommitSearchResult
            {
                ProjectKey = projectKey,
                RepositorySlug = repoSlug,
                CommitId = commitId,
                DisplayId = commit.DisplayId ?? string.Empty,
                Message = commit.Message ?? string.Empty,
                AuthorName = commit.Author?.Name ?? "Unknown",
                AuthorEmail = commit.Author?.EmailAddress ?? string.Empty,
                Timestamp = commit.AuthorTimestamp
            });

            if (results.Count >= limit)
            {
                break;
            }
        }
    }

    private static bool MatchesCommitFilters(
        Bitbucket.Net.Models.Core.Projects.Commit commit,
        string? messageContains,
        string? author,
        DateTimeOffset? since,
        DateTimeOffset? until)
    {
        // Message filter
        if (!string.IsNullOrWhiteSpace(messageContains))
        {
            var message = commit.Message ?? string.Empty;
            if (!message.Contains(messageContains, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Author filter
        if (!string.IsNullOrWhiteSpace(author))
        {
            var authorName = commit.Author?.Name ?? string.Empty;
            var authorEmail = commit.Author?.EmailAddress ?? string.Empty;

            if (!authorName.Contains(author, StringComparison.OrdinalIgnoreCase) &&
                !authorEmail.Contains(author, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Date filters
        var commitDate = commit.AuthorTimestamp;
        if (since.HasValue && commitDate < since.Value)
        {
            return false;
        }

        if (until.HasValue && commitDate > until.Value)
        {
            return false;
        }

        return true;
    }

    private static string FormatCommitSearchResults(
        List<CommitSearchResult> results,
        string projectKey,
        string? repositorySlug,
        string? messageContains,
        string? author,
        DateTimeOffset? since,
        DateTimeOffset? until,
        int limit)
    {
        var sb = new StringBuilder();
        var scope = string.IsNullOrWhiteSpace(repositorySlug)
            ? $"project '{projectKey}'"
            : $"{projectKey}/{repositorySlug}";

        sb.AppendLine($"# Commit Search Results");
        sb.AppendLine($"**Scope:** {scope}");

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(messageContains)) filters.Add($"message contains '{messageContains}'");
        if (!string.IsNullOrWhiteSpace(author)) filters.Add($"author matches '{author}'");
        if (since.HasValue) filters.Add($"since {since:yyyy-MM-dd}");
        if (until.HasValue) filters.Add($"until {until:yyyy-MM-dd}");

        sb.AppendLine($"**Filters:** {string.Join(", ", filters)}");
        sb.AppendLine();

        if (results.Count == 0)
        {
            sb.AppendLine("No commits found matching the search criteria.");
            return sb.ToString();
        }

        sb.AppendLine($"**Found {results.Count} commit(s)**");
        if (results.Count >= limit)
        {
            sb.AppendLine($"_Results limited to {limit}. Narrow your search or increase limit._");
        }
        sb.AppendLine();

        // Group by repository if searching across project
        var grouped = results.GroupBy(r => r.RepositorySlug);

        foreach (var repoGroup in grouped)
        {
            if (string.IsNullOrWhiteSpace(repositorySlug))
            {
                sb.AppendLine($"## {projectKey}/{repoGroup.Key}");
                sb.AppendLine();
            }

            foreach (var commit in repoGroup)
            {
                var messageSummary = commit.Message.Split('\n')[0];
                if (messageSummary.Length > 80)
                {
                    messageSummary = messageSummary[..77] + "...";
                }

                sb.AppendLine($"### `{commit.DisplayId}` - {messageSummary}");
                sb.AppendLine($"**Author:** {commit.AuthorName} <{commit.AuthorEmail}>");
                sb.AppendLine($"**Date:** {commit.Timestamp:yyyy-MM-dd HH:mm}");

                // Include full message if it has multiple lines
                if (commit.Message.Contains('\n'))
                {
                    var fullMessage = commit.Message.Trim();
                    if (fullMessage.Length > 500)
                    {
                        fullMessage = fullMessage[..497] + "...";
                    }
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(fullMessage);
                    sb.AppendLine("```");
                }

                sb.AppendLine();
            }
        }

        return ResponseTruncation.TruncateIfNeeded(sb.ToString(), ResponseTruncation.SearchTruncationHint);
    }

    #endregion

    #region Private Helpers — PR Search

    private async Task<List<PullRequestSearchResult>> SearchPullRequestsInRepositoryAsync(
        string projectKey,
        string repoSlug,
        string? textContains,
        string? author,
        PullRequestStates apiState,
        string? targetBranch,
        string? sourceBranch,
        int limit,
        CancellationToken cancellationToken)
    {
        var results = new List<PullRequestSearchResult>();

        // Use streaming API for early termination when limit is reached
        await foreach (var pr in Client.PullRequests(projectKey, repoSlug)
            .InState(apiState)
            .OrderBy(PullRequestOrders.Newest)
            .IncludeAttributes(true)
            .IncludeProperties(false)
            .StreamAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Apply filters
            if (!MatchesPullRequestFilters(pr, textContains, author, targetBranch, sourceBranch))
            {
                continue;
            }

            results.Add(new PullRequestSearchResult
            {
                ProjectKey = projectKey,
                RepositorySlug = repoSlug,
                Id = pr.Id,
                Title = pr.Title ?? string.Empty,
                Description = pr.Description ?? string.Empty,
                State = pr.State.ToString().ToUpperInvariant(),
                AuthorName = pr.Author?.User?.DisplayName ?? pr.Author?.User?.Name ?? "Unknown",
                AuthorSlug = pr.Author?.User?.Slug ?? string.Empty,
                SourceBranch = ToolHelpers.FormatBranchRef(pr.FromRef?.Id),
                TargetBranch = ToolHelpers.FormatBranchRef(pr.ToRef?.Id),
                CreatedDate = pr.CreatedDate ?? DateTimeOffset.MinValue,
                UpdatedDate = pr.UpdatedDate ?? DateTimeOffset.MinValue,
                ReviewerCount = pr.Reviewers?.Count ?? 0,
                Url = pr.Links?.Self?.FirstOrDefault()?.Href
            });

            // Early termination
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private static bool TryParsePullRequestState(string state, out PullRequestStates result)
    {
        result = state switch
        {
            "OPEN" => PullRequestStates.Open,
            "MERGED" => PullRequestStates.Merged,
            "DECLINED" => PullRequestStates.Declined,
            "ALL" => PullRequestStates.All,
            _ => PullRequestStates.All
        };

        return state is "OPEN" or "MERGED" or "DECLINED" or "ALL";
    }

    private static bool MatchesPullRequestFilters(
        PullRequest pr,
        string? textContains,
        string? author,
        string? targetBranch,
        string? sourceBranch)
    {
        // Text filter (title or description)
        if (!string.IsNullOrWhiteSpace(textContains))
        {
            var title = pr.Title ?? string.Empty;
            var description = pr.Description ?? string.Empty;

            if (!title.Contains(textContains, StringComparison.OrdinalIgnoreCase) &&
                !description.Contains(textContains, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Author filter
        if (!string.IsNullOrWhiteSpace(author))
        {
            var authorName = pr.Author?.User?.DisplayName ?? string.Empty;
            var authorUsername = pr.Author?.User?.Name ?? string.Empty;
            var authorSlug = pr.Author?.User?.Slug ?? string.Empty;

            if (!authorName.Contains(author, StringComparison.OrdinalIgnoreCase) &&
                !authorUsername.Contains(author, StringComparison.OrdinalIgnoreCase) &&
                !authorSlug.Contains(author, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Target branch filter
        if (!string.IsNullOrWhiteSpace(targetBranch))
        {
            var prTargetBranch = ToolHelpers.FormatBranchRef(pr.ToRef?.Id);
            if (!prTargetBranch.Equals(targetBranch, StringComparison.OrdinalIgnoreCase) &&
                !prTargetBranch.Contains(targetBranch, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Source branch filter
        if (!string.IsNullOrWhiteSpace(sourceBranch))
        {
            var prSourceBranch = ToolHelpers.FormatBranchRef(pr.FromRef?.Id);
            if (!prSourceBranch.Equals(sourceBranch, StringComparison.OrdinalIgnoreCase) &&
                !prSourceBranch.Contains(sourceBranch, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatPullRequestSearchResults(
        List<PullRequestSearchResult> results,
        string projectKey,
        string? repositorySlug,
        string? textContains,
        string? author,
        string state,
        string? targetBranch,
        string? sourceBranch,
        int limit)
    {
        var sb = new StringBuilder();
        var scope = string.IsNullOrWhiteSpace(repositorySlug)
            ? $"project '{projectKey}'"
            : $"{projectKey}/{repositorySlug}";

        sb.AppendLine("# Pull Request Search Results");
        sb.AppendLine($"**Scope:** {scope}");

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(textContains)) filters.Add($"text contains '{textContains}'");
        if (!string.IsNullOrWhiteSpace(author)) filters.Add($"author matches '{author}'");
        if (state != "ALL") filters.Add($"state = {state}");
        if (!string.IsNullOrWhiteSpace(targetBranch)) filters.Add($"target branch = '{targetBranch}'");
        if (!string.IsNullOrWhiteSpace(sourceBranch)) filters.Add($"source branch = '{sourceBranch}'");

        if (filters.Count > 0)
        {
            sb.AppendLine($"**Filters:** {string.Join(", ", filters)}");
        }
        sb.AppendLine();

        if (results.Count == 0)
        {
            sb.AppendLine("No pull requests found matching the search criteria.");
            return sb.ToString();
        }

        sb.AppendLine($"**Found {results.Count} pull request(s)**");
        if (results.Count >= limit)
        {
            sb.AppendLine($"_Results limited to {limit}. Narrow your search or increase limit._");
        }
        sb.AppendLine();

        // Group by repository if searching across project
        var grouped = results.GroupBy(r => r.RepositorySlug);

        foreach (var repoGroup in grouped)
        {
            if (string.IsNullOrWhiteSpace(repositorySlug))
            {
                sb.AppendLine($"## {projectKey}/{repoGroup.Key}");
                sb.AppendLine();
            }

            foreach (var pr in repoGroup)
            {
                var stateIcon = pr.State switch
                {
                    "OPEN" => "🟢",
                    "MERGED" => "🟣",
                    "DECLINED" => "🔴",
                    _ => "⚪"
                };

                sb.AppendLine($"### {stateIcon} PR #{pr.Id}: {pr.Title}");
                sb.AppendLine($"**State:** {pr.State} | **Author:** {pr.AuthorName}");
                sb.AppendLine($"**Branch:** `{pr.SourceBranch}` → `{pr.TargetBranch}`");
                sb.AppendLine($"**Updated:** {pr.UpdatedDate:yyyy-MM-dd HH:mm} | **Reviewers:** {pr.ReviewerCount}");

                if (!string.IsNullOrWhiteSpace(pr.Url))
                {
                    sb.AppendLine($"**Link:** {pr.Url}");
                }

                // Include description snippet if it exists and text search was used
                if (!string.IsNullOrWhiteSpace(textContains) && !string.IsNullOrWhiteSpace(pr.Description))
                {
                    var descSnippet = pr.Description.Trim();
                    if (descSnippet.Length > 200)
                    {
                        descSnippet = descSnippet[..197] + "...";
                    }
                    sb.AppendLine();
                    sb.AppendLine($"> {descSnippet.Replace("\n", "\n> ")}");
                }

                sb.AppendLine();
            }
        }

        return ResponseTruncation.TruncateIfNeeded(sb.ToString(), ResponseTruncation.SearchTruncationHint);
    }

    #endregion

    #region Inner Types

    private sealed class SearchMatch
    {
        public required string FilePath { get; init; }
        public required int LineNumber { get; init; }
        public required string MatchedLine { get; init; }
        public string[] ContextBefore { get; init; } = [];
        public string[] ContextAfter { get; set; } = [];
    }

    private sealed class CommitSearchResult
    {
        public required string ProjectKey { get; init; }
        public required string RepositorySlug { get; init; }
        public required string CommitId { get; init; }
        public required string DisplayId { get; init; }
        public required string Message { get; init; }
        public required string AuthorName { get; init; }
        public required string AuthorEmail { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }

    private sealed class PullRequestSearchResult
    {
        public required string ProjectKey { get; init; }
        public required string RepositorySlug { get; init; }
        public required int Id { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string State { get; init; }
        public required string AuthorName { get; init; }
        public required string AuthorSlug { get; init; }
        public required string SourceBranch { get; init; }
        public required string TargetBranch { get; init; }
        public required DateTimeOffset CreatedDate { get; init; }
        public required DateTimeOffset UpdatedDate { get; init; }
        public required int ReviewerCount { get; init; }
        public string? Url { get; init; }
    }

    #endregion
}