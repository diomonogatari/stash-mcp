namespace StashMcpServer.Services;

/// <summary>
/// Derives the logical invalidation group a cache key belongs to, so a single group
/// eviction atomically clears every variant (all <c>limit=</c> suffixes, all PR-context
/// flag combinations) instead of enumerating keys by hand. Keys with no group simply
/// expire by TTL and are not affected by write invalidation.
/// </summary>
public static class CacheGroups
{
    // Per pull-request-item keys — grouped by (project, repo, prId). The trailing colon on
    // "pr:" keeps it from matching "pr-list:", "pr-details:", etc.
    private static readonly string[] PrItemPrefixes =
    [
        "pr:",
        "pr-details:",
        "pr-comments:",
        "pr-activities:",
        "pr-tasks:",
        "pr-changes:",
        "pr-context:",
        "pr-blocker-comments:",
        "pr-merge-base:",
        "pr-jira:",
    ];

    /// <summary>Group id for all cache entries of a single pull request.</summary>
    public static string PrItemGroup(string projectKey, string repoSlug, long prId) =>
        $"pritem:{projectKey}:{repoSlug}:{prId}";

    /// <summary>Group id for the pull-request list views of a repository (all states and limits).</summary>
    public static string PrListGroup(string projectKey, string repoSlug) =>
        $"prlist:{projectKey}:{repoSlug}";

    /// <summary>
    /// Returns the invalidation group for a cache key, or <c>null</c> if the key is not part of
    /// a write-invalidated group (project keys and repo slugs do not contain ':').
    /// </summary>
    public static string? GroupFor(string cacheKey)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);

        if (cacheKey.StartsWith("pr-list:", StringComparison.Ordinal))
        {
            // pr-list:{project}:{repo}:{state}[:limit=N]
            var parts = cacheKey.Split(':');
            return parts.Length >= 3 ? PrListGroup(parts[1], parts[2]) : null;
        }

        foreach (var prefix in PrItemPrefixes)
        {
            if (cacheKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                // {prefix}{project}:{repo}:{prId}[:...]
                var parts = cacheKey.Split(':');
                return parts.Length >= 4 && long.TryParse(parts[3], out var prId)
                    ? PrItemGroup(parts[1], parts[2], prId)
                    : null;
            }
        }

        return null;
    }
}