using StashMcpServer.Tools;

namespace StashMcpServer.Tests.Tools;

public class ToolHelpersTests
{
    [Theory]
    [InlineData("refs/heads/main", "refs/heads/main")]
    [InlineData("refs/tags/v1.0", "refs/tags/v1.0")]
    [InlineData("main", "refs/heads/main")]
    [InlineData("feature/test", "refs/heads/feature/test")]
    public void NormalizeRef_WithBranchNames_ReturnsExpectedRef(string input, string expected)
    {
        var result = ToolHelpers.NormalizeRef(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("abc1234")]
    [InlineData("abc1234567890abc1234567890abc1234567890a")]
    public void NormalizeRef_WithCommitHash_ReturnsUnchanged(string commitHash)
    {
        var result = ToolHelpers.NormalizeRef(commitHash, allowPlainCommit: true);

        Assert.Equal(commitHash, result);
    }

    [Fact]
    public void NormalizeRef_WithCommitHashAndAllowPlainCommitFalse_AddsPrefx()
    {
        var result = ToolHelpers.NormalizeRef("abc1234", allowPlainCommit: false);

        Assert.Equal("refs/heads/abc1234", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeRef_WithNullOrWhitespace_ThrowsArgumentException(string? input)
    {
        Assert.Throws<ArgumentException>(() => ToolHelpers.NormalizeRef(input));
    }

    [Theory]
    [InlineData("abc1234", true)]
    [InlineData("abc1234567890abc1234567890abc1234567890a", true)]
    [InlineData("ABCDEF1", true)]
    [InlineData("abc123", false)]
    [InlineData("xyz1234", false)]
    [InlineData("abc12345678901234567890123456789012345678901", false)]
    public void LooksLikeCommitId_ReturnsExpected(string value, bool expected)
    {
        Assert.Equal(expected, ToolHelpers.LooksLikeCommitId(value));
    }

    [Theory]
    [InlineData("image.png", true)]
    [InlineData("library.dll", true)]
    [InlineData("app.exe", true)]
    [InlineData("readme.md", false)]
    [InlineData("Program.cs", false)]
    [InlineData("package.json", false)]
    public void IsLikelyBinary_WithVariousExtensions_ReturnsExpected(string filename, bool expected)
    {
        Assert.Equal(expected, ToolHelpers.IsLikelyBinary(filename));
    }

    [Fact]
    public void CreateRepositoryReference_SetsSlugAndProjectKey()
    {
        var repo = ToolHelpers.CreateRepositoryReference("PROJ", "my-repo");

        Assert.Equal("my-repo", repo.Slug);
        Assert.Equal("PROJ", repo.Project?.Key);
    }

    [Theory]
    [InlineData("user1,user2,user3", 3)]
    [InlineData("user1", 1)]
    [InlineData("user1, user2, user3", 3)]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void BuildReviewers_FromString_ReturnsExpectedCount(string? input, int expectedCount)
    {
        var reviewers = ToolHelpers.BuildReviewers(input);

        Assert.Equal(expectedCount, reviewers.Count);
    }

    [Fact]
    public void BuildReviewers_DeduplicatesCaseInsensitive()
    {
        var reviewers = ToolHelpers.BuildReviewers("user1,USER1,User1");

        Assert.Single(reviewers);
    }

    [Theory]
    [InlineData("user1,user2", new[] { "user1", "user2" })]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    public void ParseReviewerList_ReturnsExpected(string? input, string[] expected)
    {
        var result = ToolHelpers.ParseReviewerList(input).ToArray();

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/heads/feature/test", "feature/test")]
    [InlineData("refs/tags/v1.0", "refs/tags/v1.0")]
    [InlineData(null, "(unknown)")]
    [InlineData("", "(unknown)")]
    public void FormatBranchRef_ReturnsExpected(string? input, string expected)
    {
        Assert.Equal(expected, ToolHelpers.FormatBranchRef(input));
    }
}