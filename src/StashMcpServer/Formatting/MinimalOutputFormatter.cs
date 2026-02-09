using Bitbucket.Net.Models.Core.Projects;
using System.Text;

namespace StashMcpServer.Formatting;

/// <summary>
/// Provides minimal output formatting for reducing token consumption in LLM responses.
/// Minimal format uses compact representations with essential fields only.
/// </summary>
public static class MinimalOutputFormatter
{
    /// <summary>
    /// Formats a list of repositories in minimal format.
    /// Output: One line per repo with slug and key info.
    /// </summary>
    public static string FormatRepositories(IEnumerable<Repository> repositories, string projectKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Repositories in {projectKey}:");
        foreach (var repo in repositories)
        {
            sb.AppendLine($"- {repo.Slug}: {repo.Name ?? repo.Slug}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats a list of pull requests in minimal format.
    /// Output: "PR#ID: title [state] by author"
    /// </summary>
    public static string FormatPullRequests(IEnumerable<PullRequest> pullRequests, string projectKey, string repoSlug, string stateFilter)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PRs [{stateFilter}] {projectKey}/{repoSlug}:");
        foreach (var pr in pullRequests)
        {
            var stateIcon = pr.State switch
            {
                PullRequestStates.Open => "O",
                PullRequestStates.Merged => "M",
                PullRequestStates.Declined => "D",
                _ => "?"
            };
            var author = pr.Author?.User?.Name ?? "?";
            sb.AppendLine($"- #{pr.Id}: {pr.Title} [{stateIcon}] @{author}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats a list of branches in minimal format.
    /// Output: One branch name per line.
    /// </summary>
    public static string FormatBranches(IEnumerable<Branch> branches, string projectKey, string repoSlug, bool showDefault = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Branches in {projectKey}/{repoSlug}:");
        foreach (var branch in branches)
        {
            var defaultMarker = (showDefault && branch.IsDefault) ? " *" : string.Empty;
            sb.AppendLine($"- {branch.DisplayId}{defaultMarker}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats a list of tags in minimal format.
    /// Output: One tag name per line.
    /// </summary>
    public static string FormatTags(IEnumerable<Tag> tags, string projectKey, string repoSlug)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Tags in {projectKey}/{repoSlug}:");
        foreach (var tag in tags)
        {
            sb.AppendLine($"- {tag.DisplayId}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats a file listing in minimal format.
    /// Output: File paths only, one per line.
    /// </summary>
    public static string FormatFiles(IEnumerable<string> filePaths, string projectKey, string repoSlug, string? refName, int totalCount)
    {
        var sb = new StringBuilder();
        var refInfo = string.IsNullOrWhiteSpace(refName) ? "" : $"@{refName}";
        sb.AppendLine($"Files in {projectKey}/{repoSlug}{refInfo} ({totalCount} total):");
        foreach (var path in filePaths)
        {
            sb.AppendLine(path);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats projects in minimal format.
    /// Output: "KEY: Name"
    /// </summary>
    public static string FormatProjects(IEnumerable<Project> projects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Projects:");
        foreach (var project in projects)
        {
            sb.AppendLine($"- {project.Key}: {project.Name}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats commit search results in minimal format.
    /// Output: "shortHash: first line of message"
    /// </summary>
    public static string FormatCommits(IEnumerable<Commit> commits, string projectKey, string? repoSlug)
    {
        var sb = new StringBuilder();
        var scope = string.IsNullOrWhiteSpace(repoSlug) ? projectKey : $"{projectKey}/{repoSlug}";
        sb.AppendLine($"Commits in {scope}:");
        foreach (var commit in commits)
        {
            var shortHash = commit.DisplayId ?? commit.Id?[..7] ?? "?";
            var message = (commit.Message ?? "").Split('\n', 2)[0];
            if (message.Length > 60)
            {
                message = message[..57] + "...";
            }
            sb.AppendLine($"- {shortHash}: {message}");
        }
        return sb.ToString();
    }
}