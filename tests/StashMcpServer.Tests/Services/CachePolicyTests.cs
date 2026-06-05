using StashMcpServer.Services;

namespace StashMcpServer.Tests.Services;

public class CachePolicyTests
{
    [Theory]
    // Static — immutable-by-hash or slow-changing lists
    [InlineData("branches:P:R:filter=:limit=25", CacheDuration.Static)]
    [InlineData("tags:P:R:limit=10", CacheDuration.Static)]
    [InlineData("files:P:R:main:path=:limit=100", CacheDuration.Static)]
    [InlineData("file-content:P:R:src/x.cs:main", CacheDuration.Static)]
    [InlineData("commit:P:R:abc123", CacheDuration.Static)]
    [InlineData("commit-changes:P:R:abc123:limit=50", CacheDuration.Static)]
    // Short — CI/build status
    [InlineData("build-status:abc123:limit=10", CacheDuration.Short)]
    [InlineData("build-stats:abc123", CacheDuration.Short)]
    [InlineData("batch-build-stats:a,b,c", CacheDuration.Short)]
    [InlineData("repo-builds:P:R:main:25", CacheDuration.Short)]
    // Default — everything else, including the lookalike prefixes that must NOT be Static/Short
    [InlineData("commit-list:P:R:main:_", CacheDuration.Default)]
    [InlineData("commit-search:P:R:_:_:_:_:_", CacheDuration.Default)]
    [InlineData("repo-commits:P:R:main:25", CacheDuration.Default)]
    [InlineData("pr-list:P:R:OPEN:limit=25", CacheDuration.Default)]
    [InlineData("pr-details:P:R:1", CacheDuration.Default)]
    [InlineData("my-prs:AUTHOR:OPEN", CacheDuration.Default)]
    public void Categorize_MapsByPrefix(string cacheKey, CacheDuration expected)
    {
        Assert.Equal(expected, CachePolicy.Categorize(cacheKey));
    }
}
