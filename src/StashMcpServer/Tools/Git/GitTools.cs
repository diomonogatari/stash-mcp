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
public class GitTools(
    ILogger<GitTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    BitbucketClient client,
    IServerSettings serverSettings)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    [McpServerTool(Name = "list_branches"), Description("List branches in a repository, optionally filtered by name.")]
    public async Task<string> ListBranchesAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("Optional filter to match branch names (case-insensitive contains).")] string? filter = null,
        [Description("Maximum number of branches to return (default 50).")] int limit = 50,
        [Description("Return minimal output (branch names only). Default is false.")] bool minimalOutput = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var cappedLimit = Math.Clamp(limit, 1, 200);

        LogToolInvocation(nameof(ListBranchesAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(filter), filter),
            (nameof(limit), cappedLimit),
            (nameof(minimalOutput), minimalOutput));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"{CacheKeys.Branches(normalizedProjectKey, normalizedSlug)}:filter={filter}:limit={cappedLimit}";
        var paginatedBranches = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetBranchesStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                filterText: filter,
                cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var branches = paginatedBranches.Items.ToList();

        if (branches.Count == 0)
        {
            return filter is not null
                ? $"No branches matching '{filter}' found in {normalizedProjectKey}/{normalizedSlug}."
                : $"No branches found in {normalizedProjectKey}/{normalizedSlug}.";
        }

        var defaultBranch = await CacheService.GetDefaultBranchNameAsync(normalizedProjectKey, normalizedSlug, cancellationToken).ConfigureAwait(false);

        if (minimalOutput)
        {
            return MinimalOutputFormatter.FormatBranches(branches, normalizedProjectKey, normalizedSlug, showDefault: true);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Branches in {normalizedProjectKey}/{normalizedSlug} ({branches.Count})");
        sb.AppendLine(new string('-', 60));

        foreach (var branch in branches.OrderBy(b => b.DisplayId, StringComparer.OrdinalIgnoreCase))
        {
            var isDefault = branch.IsDefault || string.Equals(branch.DisplayId, defaultBranch, StringComparison.OrdinalIgnoreCase);
            var defaultMarker = isDefault ? " (default)" : "";
            var commit = branch.LatestCommit?.Length >= 7 ? branch.LatestCommit[..7] : branch.LatestCommit ?? "?";

            sb.AppendLine($"- {branch.DisplayId} [{commit}]{defaultMarker}");
        }

        if (paginatedBranches.HasMore)
        {
            sb.AppendLine();
            sb.AppendLine("More branches available — increase limit for more results.");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list_tags"), Description("List tags to identify release points.")]
    public async Task<string> ListTagsAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("Optional filter applied to tag names.")] string? filter = null,
        [Description("Maximum number of tags to return (default 50).")] int limit = 50,
        [Description("Return minimal output (tag names only). Default is false.")] bool minimalOutput = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var cappedLimit = Math.Clamp(limit, 1, 200);

        LogToolInvocation(nameof(ListTagsAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(filter), filter),
            (nameof(limit), cappedLimit),
            (nameof(minimalOutput), minimalOutput));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"{CacheKeys.Tags(normalizedProjectKey, normalizedSlug)}:filter={filter}:limit={cappedLimit}";
        var paginatedTags = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetProjectRepositoryTagsStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                filterText: filter ?? string.Empty,
                orderBy: BranchOrderBy.Modification,
                cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var tags = paginatedTags.Items.ToList();

        if (tags.Count == 0)
        {
            return filter is not null
                ? $"No tags matching '{filter}' found in {normalizedProjectKey}/{normalizedSlug}."
                : $"No tags found in {normalizedProjectKey}/{normalizedSlug}.";
        }

        if (minimalOutput)
        {
            return MinimalOutputFormatter.FormatTags(tags, normalizedProjectKey, normalizedSlug);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Tags in {normalizedProjectKey}/{normalizedSlug} ({tags.Count})");
        sb.AppendLine(new string('-', 60));

        foreach (var tag in tags)
        {
            var commit = tag.LatestCommit?.Length >= 7 ? tag.LatestCommit[..7] : tag.LatestCommit ?? "?";
            sb.AppendLine($"- {tag.DisplayId} [{commit}]");
        }

        if (paginatedTags.HasMore)
        {
            sb.AppendLine();
            sb.AppendLine("More tags available — increase limit for more results.");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list_files"), Description("List files in a repository at a specific path or root.")]
    public async Task<string> ListFilesAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("Path within the repository to list files from. Use empty string or '/' for root.")] string? path = null,
        [Description("Commit hash or branch name. Defaults to the repository's default branch.")] string? at = null,
        [Description("Maximum number of files to return (default 500).")] int limit = 500,
        [Description("Return minimal output (file paths only). Default is false.")] bool minimalOutput = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);
        var cappedLimit = Math.Clamp(limit, 1, 5000);
        var effectivePath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();

        var effectiveRef = at;
        if (string.IsNullOrWhiteSpace(effectiveRef))
        {
            effectiveRef = await CacheService.GetDefaultBranchNameAsync(normalizedProjectKey, normalizedSlug, cancellationToken).ConfigureAwait(false);
        }

        LogToolInvocation(nameof(ListFilesAsync), (nameof(projectKey), projectKey), (nameof(repositorySlug), repositorySlug), (nameof(path), effectivePath), (nameof(at), effectiveRef), (nameof(limit), cappedLimit));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"{CacheKeys.Files(normalizedProjectKey, normalizedSlug, effectiveRef)}:path={effectivePath}:ref={effectiveRef}:limit={cappedLimit}";
        var files = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ => await Client.GetRepositoryFilesAsync(normalizedProjectKey, normalizedSlug, effectiveRef, limit: cappedLimit)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var fileList = files.ToList();

        if (fileList.Count == 0)
        {
            return $"No files found in {normalizedProjectKey}/{normalizedSlug} at path '{effectivePath}'.";
        }

        var sorted = fileList.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        if (minimalOutput)
        {
            return MinimalOutputFormatter.FormatFiles(sorted, normalizedProjectKey, normalizedSlug, effectiveRef, fileList.Count);
        }

        var sb = new StringBuilder();
        var refInfo = string.IsNullOrWhiteSpace(effectiveRef) ? "" : $" @ {effectiveRef}";
        sb.AppendLine($"Files in {normalizedProjectKey}/{normalizedSlug}{refInfo} ({fileList.Count} files)");
        sb.AppendLine(new string('-', 60));

        foreach (var file in sorted)
        {
            sb.AppendLine($"  {file}");
        }

        return sb.ToString();
    }
}