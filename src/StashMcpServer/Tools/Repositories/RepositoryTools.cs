using Bitbucket.Net;
using Bitbucket.Net.Common.Mcp;
using ModelContextProtocol.Server;
using StashMcpServer.Formatting;
using StashMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace StashMcpServer.Tools;

[McpServerToolType]
public class RepositoryTools(
    ILogger<RepositoryTools> logger,
    IBitbucketCacheService cacheService,
    IResilientApiService resilientApi,
    BitbucketClient client,
    IServerSettings serverSettings)
    : ToolBase(logger, cacheService, resilientApi, client, serverSettings)
{
    [McpServerTool(Name = "list_repositories"), Description("List repositories within a specific project.")]
    public async Task<string> ListRepositoriesAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("Return minimal output (repository slugs only). Default is false.")] bool minimalOutput = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        LogToolInvocation(nameof(ListRepositoriesAsync), (nameof(projectKey), projectKey), (nameof(minimalOutput), minimalOutput));

        cancellationToken.ThrowIfCancellationRequested();

        var repositories = CacheService
            .GetRepositories(normalizedProjectKey)
            .ToList();

        if (repositories.Count == 0)
        {
            Logger.LogInformation("Repositories for {ProjectKey} not found in cache. Fetching from Bitbucket...", normalizedProjectKey);
            var remoteRepositories = new List<Bitbucket.Net.Models.Core.Projects.Repository>();
            await foreach (var repo in Client
                .GetProjectRepositoriesStreamAsync(normalizedProjectKey, cancellationToken: cancellationToken)
                .TakeAsync(1000, cancellationToken)
                .ConfigureAwait(false))
            {
                remoteRepositories.Add(repo);
            }

            repositories = remoteRepositories;
            CacheService.StoreRepositories(normalizedProjectKey, repositories);
        }

        if (repositories.Count == 0)
        {
            return $"No repositories found for project '{normalizedProjectKey}'.";
        }

        var sortedRepos = repositories.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Minimal output mode
        if (minimalOutput)
        {
            return MinimalOutputFormatter.FormatRepositories(sortedRepos, normalizedProjectKey);
        }

        // Standard detailed output
        var builder = new StringBuilder();
        builder.AppendLine($"Repositories in {normalizedProjectKey} ({repositories.Count})");
        builder.AppendLine(new string('-', 60));

        foreach (var repository in sortedRepos)
        {
            var visibility = repository.Public ? "public" : "private";
            var scm = string.IsNullOrWhiteSpace(repository.ScmId) ? "unknown" : repository.ScmId;
            builder.AppendLine($"- {repository.Name} [{repository.Slug}] · SCM={scm}, {visibility}");
        }

        return builder.ToString();
    }

    [McpServerTool(Name = "get_file_content"), Description("Gets the raw content of a file in a repository at a specific commit or branch.")]
    public async Task<string> GetFileContentAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("The path to the file.")] string filePath,
        [Description("The commit hash or branch name to retrieve the content at. Defaults to the repository's default branch.")] string? at = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        var effectiveRef = at;
        if (string.IsNullOrWhiteSpace(effectiveRef))
        {
            effectiveRef = await CacheService.GetDefaultBranchNameAsync(normalizedProjectKey, normalizedSlug, cancellationToken).ConfigureAwait(false);
        }

        LogToolInvocation(nameof(GetFileContentAsync), (nameof(projectKey), projectKey), (nameof(repositorySlug), repositorySlug), (nameof(filePath), filePath), (nameof(at), effectiveRef));

        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CacheKeys.FileContent(normalizedProjectKey, normalizedSlug, filePath, effectiveRef);
        var content = await ResilientApi.ExecuteAsync(
            cacheKey,
            async _ =>
            {
                using var stream = await Client.RetrieveRawContentAsync(normalizedProjectKey, normalizedSlug, filePath, effectiveRef).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            },
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Truncate large file content to prevent context overflow
        return ResponseTruncation.TruncateFileContent(content);
    }

    /// <summary>
    /// Workflow-oriented tool that provides a comprehensive repository overview in a single call.
    /// Useful when exploring a new repository or getting oriented with a codebase.
    /// </summary>
    [McpServerTool(Name = "get_repository_overview")]
    [Description("Get comprehensive repository information including default branch, recent branches, tags, and structure. Use this to quickly understand a repository's state.")]
    public async Task<string> GetRepositoryOverviewAsync(
        [Description("The key of the Bitbucket project.")] string projectKey,
        [Description("The slug of the Bitbucket repository.")] string repositorySlug,
        [Description("Maximum number of branches to include (default: 10).")] int branchLimit = 10,
        [Description("Maximum number of tags to include (default: 5).")] int tagLimit = 5,
        [Description("Include recent open pull requests (default: true).")] bool includeOpenPRs = true,
        [Description("Maximum open PRs to include (default: 5).")] int prLimit = 5,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectKey = NormalizeProjectKey(projectKey);
        var normalizedSlug = NormalizeRepositorySlug(normalizedProjectKey, repositorySlug);

        LogToolInvocation(nameof(GetRepositoryOverviewAsync),
            (nameof(projectKey), projectKey),
            (nameof(repositorySlug), repositorySlug),
            (nameof(branchLimit), branchLimit),
            (nameof(tagLimit), tagLimit),
            (nameof(includeOpenPRs), includeOpenPRs));

        var cappedBranchLimit = Math.Clamp(branchLimit, 1, 50);
        var cappedTagLimit = Math.Clamp(tagLimit, 1, 20);
        var cappedPrLimit = Math.Clamp(prLimit, 1, 20);

        cancellationToken.ThrowIfCancellationRequested();

        // Parallel fetch all repository data for efficiency
        var branchesCacheKey = $"{CacheKeys.Branches(normalizedProjectKey, normalizedSlug)}:limit={cappedBranchLimit}";
        var branchesTask = ResilientApi.ExecuteAsync(
            branchesCacheKey,
            async _ => await Client.GetBranchesStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedBranchLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken);

        var tagsCacheKey = $"{CacheKeys.Tags(normalizedProjectKey, normalizedSlug)}:limit={cappedTagLimit}";
        var tagsTask = ResilientApi.ExecuteAsync(
            tagsCacheKey,
            async _ => await Client.GetProjectRepositoryTagsStreamAsync(
                normalizedProjectKey,
                normalizedSlug,
                filterText: string.Empty,
                orderBy: Bitbucket.Net.Models.Core.Projects.BranchOrderBy.Modification,
                cancellationToken: cancellationToken)
                .TakeWithPaginationAsync(cappedTagLimit, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken: cancellationToken);

        var prsCacheKey = $"{CacheKeys.PullRequestList(normalizedProjectKey, normalizedSlug, "OPEN")}:limit={cappedPrLimit}";
        var prsTask = includeOpenPRs
            ? ResilientApi.ExecuteAsync(
                prsCacheKey,
                async _ => await Client.GetPullRequestsStreamAsync(
                    normalizedProjectKey,
                    normalizedSlug,
                    state: Bitbucket.Net.Models.Core.Projects.PullRequestStates.Open,
                    cancellationToken: cancellationToken)
                    .TakeWithPaginationAsync(cappedPrLimit, cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
            : Task.FromResult(new PaginatedResult<Bitbucket.Net.Models.Core.Projects.PullRequest>([], false, null));

        await Task.WhenAll(branchesTask, tagsTask, prsTask).ConfigureAwait(false);

        var branches = (await branchesTask.ConfigureAwait(false)).Items.ToList();
        var tags = (await tagsTask.ConfigureAwait(false)).Items.ToList();
        var prs = (await prsTask.ConfigureAwait(false)).Items.ToList();

        // Get cached repository info
        var repository = CacheService.FindRepository(normalizedProjectKey, normalizedSlug);
        var defaultBranch = await CacheService.GetDefaultBranchNameAsync(normalizedProjectKey, normalizedSlug, cancellationToken).ConfigureAwait(false);

        // Build comprehensive response
        var sb = new StringBuilder();

        // Section 1: Repository Header
        sb.AppendLine($"# Repository: {normalizedProjectKey}/{normalizedSlug}");
        sb.AppendLine();

        if (repository != null)
        {
            var visibility = repository.Public ? "🌐 Public" : "🔒 Private";
            sb.AppendLine($"**Name:** {repository.Name}");
            sb.AppendLine($"**Visibility:** {visibility}");
            sb.AppendLine($"**SCM:** {repository.ScmId ?? "git"}");

            var cloneLinks = repository.Links?.Clone;
            if (cloneLinks != null)
            {
                var sshLink = cloneLinks.FirstOrDefault(l => l.Name == "ssh")?.Href;
                var httpsLink = cloneLinks.FirstOrDefault(l => l.Name == "http" || l.Name == "https")?.Href;

                if (!string.IsNullOrWhiteSpace(sshLink))
                    sb.AppendLine($"**Clone (SSH):** `{sshLink}`");
                if (!string.IsNullOrWhiteSpace(httpsLink))
                    sb.AppendLine($"**Clone (HTTPS):** `{httpsLink}`");
            }
        }

        sb.AppendLine();

        // Section 2: Default Branch
        sb.AppendLine("## Default Branch");
        sb.AppendLine($"`{defaultBranch ?? "(unknown)"}`");
        sb.AppendLine();

        // Section 3: Branches
        sb.AppendLine($"## Branches ({branches.Count} shown)");
        if (branches.Count == 0)
        {
            sb.AppendLine("No branches found.");
        }
        else
        {
            foreach (var branch in branches.OrderBy(b => b.DisplayId, StringComparer.OrdinalIgnoreCase))
            {
                var isDefault = branch.IsDefault ||
                    string.Equals(branch.DisplayId, defaultBranch, StringComparison.OrdinalIgnoreCase);
                var defaultMarker = isDefault ? " ⭐ (default)" : "";
                var commit = branch.LatestCommit?.Length >= 7 ? branch.LatestCommit[..7] : branch.LatestCommit ?? "?";

                sb.AppendLine($"- `{branch.DisplayId}` ({commit}){defaultMarker}");
            }
        }

        sb.AppendLine();

        // Section 4: Tags
        sb.AppendLine($"## Tags ({tags.Count} shown)");
        if (tags.Count == 0)
        {
            sb.AppendLine("No tags found.");
        }
        else
        {
            foreach (var tag in tags.OrderByDescending(t => t.DisplayId))
            {
                var commit = tag.LatestCommit?.Length >= 7 ? tag.LatestCommit[..7] : tag.LatestCommit ?? "?";
                sb.AppendLine($"- `{tag.DisplayId}` ({commit})");
            }
        }

        // Section 5: Open Pull Requests
        if (includeOpenPRs)
        {
            sb.AppendLine();
            sb.AppendLine($"## Open Pull Requests ({prs.Count} shown)");

            if (prs.Count == 0)
            {
                sb.AppendLine("No open pull requests.");
            }
            else
            {
                foreach (var pr in prs)
                {
                    var author = pr.Author?.User?.DisplayName ?? "Unknown";
                    var isYou = CacheService.IsCurrentUser(pr.Author?.User?.Slug);
                    var youMarker = isYou ? " (you)" : "";

                    sb.AppendLine($"- **#{pr.Id}**: {pr.Title}");
                    sb.AppendLine($"  - Author: {author}{youMarker}");
                    sb.AppendLine($"  - `{ToolHelpers.FormatBranchRef(pr.FromRef?.Id)}` → `{ToolHelpers.FormatBranchRef(pr.ToRef?.Id)}`");
                }
            }
        }

        return sb.ToString();
    }
}