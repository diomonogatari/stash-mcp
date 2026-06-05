namespace StashMcpServer.Services;

/// <summary>
/// Central policy mapping a cache key to its <see cref="CacheDuration"/> category by key prefix,
/// so slow-changing / immutable data (commits by hash, branch &amp; tag lists, file content) is
/// cached longer and fast-changing data (CI/build status) shorter — without wiring TTLs at every
/// call site. Keys with no matching prefix fall back to <see cref="CacheDuration.Default"/>.
/// </summary>
public static class CachePolicy
{
    // Immutable-by-hash or slow-changing list data. Note the trailing colon keeps prefixes
    // precise (e.g. "commit:" matches commit details but not "commit-list:" / "commit-search:").
    private static readonly string[] StaticPrefixes =
    [
        "branches:",
        "tags:",
        "files:",
        "file-content:",
        "commit:",
        "commit-changes:",
    ];

    // CI/build status, which changes quickly and should stay fresh.
    private static readonly string[] ShortPrefixes =
    [
        "build-status:",
        "build-stats:",
        "batch-build-stats:",
        "repo-builds:",
    ];

    /// <summary>
    /// Categorizes a cache key into a <see cref="CacheDuration"/> based on its prefix.
    /// </summary>
    public static CacheDuration Categorize(string cacheKey)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);

        foreach (var prefix in StaticPrefixes)
        {
            if (cacheKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                return CacheDuration.Static;
            }
        }

        foreach (var prefix in ShortPrefixes)
        {
            if (cacheKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                return CacheDuration.Short;
            }
        }

        return CacheDuration.Default;
    }
}