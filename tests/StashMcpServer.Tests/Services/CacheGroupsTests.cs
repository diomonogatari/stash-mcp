using StashMcpServer.Services;

namespace StashMcpServer.Tests.Services;

public class CacheGroupsTests
{
    [Theory]
    [InlineData("pr:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-details:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-comments:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-activities:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-tasks:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-changes:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-context:P:R:1:True:False:True:False", "pritem:P:R:1")]
    [InlineData("pr-merge-base:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-jira:P:R:1", "pritem:P:R:1")]
    [InlineData("pr-list:P:R:OPEN", "prlist:P:R")]
    [InlineData("pr-list:P:R:OPEN:limit=25", "prlist:P:R")]
    public void GroupFor_PrKeys_MapToTheirGroup(string cacheKey, string expected)
    {
        Assert.Equal(expected, CacheGroups.GroupFor(cacheKey));
    }

    [Theory]
    [InlineData("branches:P:R")]
    [InlineData("commit:P:R:abc")]
    [InlineData("build-status:abc")]
    [InlineData("my-prs:AUTHOR:OPEN")]
    [InlineData("user-search:bob:25")]
    public void GroupFor_NonGroupedKeys_ReturnNull(string cacheKey)
    {
        Assert.Null(CacheGroups.GroupFor(cacheKey));
    }
}