using StashMcpServer.Formatting;

namespace StashMcpServer.Tests.Formatting;

public class ResponseTruncationTests
{
    #region TruncateIfNeeded Tests

    [Fact]
    public void TruncateIfNeeded_WhenContentUnderLimit_ReturnsOriginal()
    {
        var content = "Short content";
        var hint = "[Truncated]";
        var result = ResponseTruncation.TruncateIfNeeded(content, hint);
        Assert.Equal(content, result);
    }

    [Fact]
    public void TruncateIfNeeded_WhenContentExactlyAtLimit_ReturnsOriginal()
    {
        var maxLength = 100;
        var content = new string('x', maxLength);
        var hint = "[Truncated]";
        var result = ResponseTruncation.TruncateIfNeeded(content, hint, maxLength);
        Assert.Equal(content, result);
    }

    [Fact]
    public void TruncateIfNeeded_WhenContentOverLimit_TruncatesWithHint()
    {
        var maxLength = 50;
        var hint = "[Truncated]";
        var content = new string('x', 100);
        var result = ResponseTruncation.TruncateIfNeeded(content, hint, maxLength);
        Assert.EndsWith(hint, result);
        Assert.True(result.Length <= maxLength);
    }

    [Fact]
    public void TruncateIfNeeded_WhenNull_ReturnsNull()
    {
        string? content = null;
        var hint = "[Truncated]";
        var result = ResponseTruncation.TruncateIfNeeded(content!, hint);
        Assert.Null(result);
    }

    [Fact]
    public void TruncateIfNeeded_WhenEmpty_ReturnsEmpty()
    {
        var content = string.Empty;
        var hint = "[Truncated]";
        var result = ResponseTruncation.TruncateIfNeeded(content, hint);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TruncateIfNeeded_WithCustomMaxLength_UsesCustomLength()
    {
        var content = "This is a medium length string for testing";
        var hint = "[...]";
        var maxLength = 20;
        var result = ResponseTruncation.TruncateIfNeeded(content, hint, maxLength);
        Assert.True(result.Length <= maxLength);
        Assert.EndsWith(hint, result);
    }

    #endregion

    #region Line Boundary Truncation Tests

    [Fact]
    public void TruncateIfNeeded_WithNewlines_TruncatesAtLineBoundary()
    {
        var maxLength = 50;
        var hint = "[T]"; // Short hint
        // Create content with newlines where the last newline is within 80% of truncation point
        var line1 = new string('a', 20) + "\n";
        var line2 = new string('b', 20) + "\n";
        var line3 = new string('c', 20);
        var content = line1 + line2 + line3;
        var result = ResponseTruncation.TruncateIfNeeded(content, hint, maxLength);
        // Should truncate at newline boundary if within 80% threshold
        Assert.EndsWith(hint, result);
    }

    [Fact]
    public void TruncateIfNeeded_WithNewlineTooEarly_DoesNotUseLineBoundary()
    {
        var maxLength = 100;
        var hint = "[Truncated]";
        // Create content where the newline is early (< 80% of truncation point)
        var content = "Line1\n" + new string('x', 150);
        var result = ResponseTruncation.TruncateIfNeeded(content, hint, maxLength);
        Assert.EndsWith(hint, result);
        Assert.True(result.Length <= maxLength);
    }

    #endregion

    #region ClampLimit Tests

    [Fact]
    public void ClampLimit_WhenZero_ReturnsDefault()
    {
        var result = ResponseTruncation.ClampLimit(0);
        Assert.Equal(ResponseTruncation.DefaultListLimit, result);
    }

    [Fact]
    public void ClampLimit_WhenNegative_ReturnsDefault()
    {
        var result = ResponseTruncation.ClampLimit(-10);
        Assert.Equal(ResponseTruncation.DefaultListLimit, result);
    }

    [Fact]
    public void ClampLimit_WhenWithinBounds_ReturnsValue()
    {
        var result = ResponseTruncation.ClampLimit(25);
        Assert.Equal(25, result);
    }

    [Fact]
    public void ClampLimit_WhenExceedsMax_ReturnsMax()
    {
        var result = ResponseTruncation.ClampLimit(500);
        Assert.Equal(ResponseTruncation.MaxListLimit, result);
    }

    [Fact]
    public void ClampLimit_WithCustomDefault_UsesCustomDefault()
    {
        var result = ResponseTruncation.ClampLimit(0, defaultLimit: 10);
        Assert.Equal(10, result);
    }

    [Fact]
    public void ClampLimit_WithCustomMax_UsesCustomMax()
    {
        var result = ResponseTruncation.ClampLimit(200, maxLimit: 150);
        Assert.Equal(150, result);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void ClampLimit_BoundaryValues_ReturnsCorrectValue(int input, int expected)
    {
        var result = ResponseTruncation.ClampLimit(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region TruncateFileContent Tests

    [Fact]
    public void TruncateFileContent_WhenUnderLimit_ReturnsOriginal()
    {
        var content = "File content here";
        var result = ResponseTruncation.TruncateFileContent(content);
        Assert.Equal(content, result);
    }

    [Fact]
    public void TruncateFileContent_WhenOverLimit_AppendsFileHint()
    {
        var maxLength = 100;
        var content = new string('x', 150);
        var result = ResponseTruncation.TruncateFileContent(content, maxLength);
        Assert.EndsWith(ResponseTruncation.FileTruncationHint, result);
    }

    #endregion

    #region TruncateSearchResults Tests

    [Fact]
    public void TruncateSearchResults_WhenUnderLimit_ReturnsOriginal()
    {
        var content = "Search results here";
        var result = ResponseTruncation.TruncateSearchResults(content);
        Assert.Equal(content, result);
    }

    [Fact]
    public void TruncateSearchResults_WhenOverLimit_AppendsSearchHint()
    {
        var maxLength = 100;
        var content = new string('x', 150);
        var result = ResponseTruncation.TruncateSearchResults(content, maxLength);
        Assert.EndsWith(ResponseTruncation.SearchTruncationHint, result);
    }

    #endregion

    #region TruncateDiff Tests

    [Fact]
    public void TruncateDiff_WhenUnderLimit_ReturnsOriginal()
    {
        var content = "Diff content here";
        var result = ResponseTruncation.TruncateDiff(content);
        Assert.Equal(content, result);
    }

    [Fact]
    public void TruncateDiff_WhenOverLimit_AppendsDiffHint()
    {
        // Use a maxLength larger than the hint to ensure truncation works
        var maxLength = 200;
        var content = new string('x', 300);
        var result = ResponseTruncation.TruncateDiff(content, maxLength);
        Assert.EndsWith(ResponseTruncation.DiffTruncationHint, result);
    }

    #endregion

    #region WouldTruncate Tests

    [Fact]
    public void WouldTruncate_WhenUnderLimit_ReturnsFalse()
    {
        var content = "Short content";
        var result = ResponseTruncation.WouldTruncate(content);
        Assert.False(result);
    }

    [Fact]
    public void WouldTruncate_WhenOverLimit_ReturnsTrue()
    {
        var content = new string('x', ResponseTruncation.MaxResponseLengthBytes + 100);
        var result = ResponseTruncation.WouldTruncate(content);
        Assert.True(result);
    }

    [Fact]
    public void WouldTruncate_WhenNull_ReturnsFalse()
    {
        var result = ResponseTruncation.WouldTruncate(null!);
        Assert.False(result);
    }

    [Fact]
    public void WouldTruncate_WithCustomMaxLength_UsesCustomLength()
    {
        var content = new string('x', 50);
        var result = ResponseTruncation.WouldTruncate(content, maxLength: 30);
        Assert.True(result);
    }

    #endregion

    #region GetAvailableLength Tests

    [Fact]
    public void GetAvailableLength_ReturnsMaxMinusHintLength()
    {
        var hint = "[Hint]";
        var maxLength = 100;
        var result = ResponseTruncation.GetAvailableLength(hint, maxLength);
        Assert.Equal(100 - hint.Length, result);
    }

    [Fact]
    public void GetAvailableLength_WithDefaultMax_UsesDefaultMax()
    {
        var hint = "[Hint]";
        var result = ResponseTruncation.GetAvailableLength(hint);
        Assert.Equal(ResponseTruncation.MaxResponseLengthBytes - hint.Length, result);
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(50 * 1024, ResponseTruncation.MaxResponseLengthBytes);
        Assert.Equal(50, ResponseTruncation.DefaultListLimit);
        Assert.Equal(100, ResponseTruncation.MaxListLimit);
    }

    [Fact]
    public void TruncationHints_AreNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(ResponseTruncation.DiffTruncationHint));
        Assert.False(string.IsNullOrEmpty(ResponseTruncation.FileTruncationHint));
        Assert.False(string.IsNullOrEmpty(ResponseTruncation.SearchTruncationHint));
        Assert.False(string.IsNullOrEmpty(ResponseTruncation.ListTruncationHint));
    }

    #endregion
}