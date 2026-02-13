using Bitbucket.Net.Models.Core.Projects;
using Bitbucket.Net.Models.Core.Users;
using Path = System.IO.Path;

namespace StashMcpServer.Tools;

/// <summary>
/// Pure static helper methods shared across tool classes.
/// </summary>
public static class ToolHelpers
{
    /// <summary>
    /// Normalizes a Git reference by ensuring it has a refs/ prefix.
    /// </summary>
    public static string NormalizeRef(string? reference, string defaultPrefix = "refs/heads/", bool allowPlainCommit = true)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Reference is required.", nameof(reference));
        }

        var trimmed = reference.Trim();
        if (trimmed.StartsWith("refs/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (allowPlainCommit && LooksLikeCommitId(trimmed))
        {
            return trimmed;
        }

        return string.Concat(defaultPrefix, trimmed);
    }

    /// <summary>
    /// Checks if a string looks like a commit hash (7-40 hex characters).
    /// </summary>
    public static bool LooksLikeCommitId(string value)
    {
        if (value.Length is < 7 or > 40)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    /// <summary>
    /// Binary file extensions to skip during search and content display.
    /// </summary>
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".bin", ".obj", ".o", ".so", ".dylib",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".tar", ".gz", ".rar", ".7z", ".jar", ".war", ".ear",
        ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".class", ".pyc", ".pdb", ".nupkg", ".snupkg",
        ".lock", ".min.js", ".min.css", ".map"
    };

    /// <summary>
    /// Checks if a file is likely a binary file based on its extension.
    /// </summary>
    public static bool IsLikelyBinary(string filename)
    {
        var ext = Path.GetExtension(filename);
        return BinaryExtensions.Contains(ext);
    }

    /// <summary>
    /// Creates a minimal Repository reference for API calls.
    /// </summary>
    public static Repository CreateRepositoryReference(string projectKey, string repositorySlug) => new()
    {
        Slug = repositorySlug,
        Project = new Project { Key = projectKey }
    };

    /// <summary>
    /// Builds a list of reviewers from a comma-separated string.
    /// </summary>
    public static List<Reviewer> BuildReviewers(string? reviewers) => BuildReviewers(ParseReviewerList(reviewers));

    /// <summary>
    /// Builds a list of reviewers from an enumerable of reviewer identifiers.
    /// </summary>
    public static List<Reviewer> BuildReviewers(IEnumerable<string> reviewers)
    {
        return reviewers
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(r => new Reviewer
            {
                User = new User
                {
                    Name = r,
                    Slug = r
                }
            })
            .ToList();
    }

    /// <summary>
    /// Parses a comma-separated reviewer list into individual reviewer identifiers.
    /// </summary>
    public static IEnumerable<string> ParseReviewerList(string? reviewers)
    {
        if (string.IsNullOrWhiteSpace(reviewers))
        {
            return Array.Empty<string>();
        }

        return reviewers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Gets the best identifier for a reviewer (slug, name, or display name).
    /// </summary>
    public static string? GetReviewerIdentifier(Reviewer reviewer)
    {
        return reviewer.User?.Slug ?? reviewer.User?.Name ?? reviewer.User?.DisplayName;
    }

    /// <summary>
    /// Formats a branch ref ID by stripping the refs/heads/ prefix.
    /// </summary>
    public static string FormatBranchRef(string? refId)
    {
        if (string.IsNullOrWhiteSpace(refId))
        {
            return "(unknown)";
        }

        const string prefix = "refs/heads/";
        return refId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? refId[prefix.Length..]
            : refId;
    }
}