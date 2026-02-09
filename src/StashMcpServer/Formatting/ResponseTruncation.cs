namespace StashMcpServer.Formatting;

/// <summary>
/// Provides response truncation and pagination utilities for large content.
/// Helps prevent context limit overflows in LLM responses.
/// </summary>
public static class ResponseTruncation
{
    /// <summary>
    /// Maximum response length in bytes. Default: 50KB.
    /// </summary>
    public const int MaxResponseLengthBytes = 50 * 1024;

    /// <summary>
    /// Maximum number of items for list operations. Default: 50.
    /// </summary>
    public const int DefaultListLimit = 50;

    /// <summary>
    /// Maximum number of items for list operations. Default: 100.
    /// </summary>
    public const int MaxListLimit = 100;

    /// <summary>
    /// Truncation hint for diff content.
    /// </summary>
    public const string DiffTruncationHint =
        "\n\n[Diff truncated at 50KB. Use get_file_content for specific files or get_pull_request_changes to see the list of changed files.]";

    /// <summary>
    /// Truncation hint for file content.
    /// </summary>
    public const string FileTruncationHint =
        "\n\n[Content truncated at 50KB. Use offset/limit parameters to read the remaining content.]";

    /// <summary>
    /// Truncation hint for search results.
    /// </summary>
    public const string SearchTruncationHint =
        "\n\n[Results truncated at 50KB. Narrow your search with more specific query or path filters.]";

    /// <summary>
    /// Truncation hint for list results.
    /// </summary>
    public const string ListTruncationHint =
        "\n\n[Results limited. Use offset parameter to paginate through additional items.]";

    /// <summary>
    /// Truncates content if it exceeds the maximum response length.
    /// </summary>
    /// <param name="content">The content to potentially truncate.</param>
    /// <param name="hint">The hint to append when truncation occurs.</param>
    /// <param name="maxLength">Optional custom max length. Defaults to MaxResponseLengthBytes.</param>
    /// <returns>The original or truncated content with hint.</returns>
    public static string TruncateIfNeeded(string content, string hint, int? maxLength = null)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        var effectiveMaxLength = maxLength ?? MaxResponseLengthBytes;

        // Account for hint length when calculating truncation point
        var truncationPoint = effectiveMaxLength - hint.Length;

        if (content.Length <= effectiveMaxLength)
        {
            return content;
        }

        // Try to truncate at a line boundary for cleaner output
        var truncated = content[..truncationPoint];
        var lastNewline = truncated.LastIndexOf('\n');

        if (lastNewline > truncationPoint * 0.8) // Only use line boundary if it's within 80% of target
        {
            truncated = truncated[..lastNewline];
        }

        return truncated + hint;
    }

    /// <summary>
    /// Truncates diff content with appropriate hint.
    /// </summary>
    public static string TruncateDiff(string diffContent, int? maxLength = null)
    {
        return TruncateIfNeeded(diffContent, DiffTruncationHint, maxLength);
    }

    /// <summary>
    /// Truncates file content with appropriate hint.
    /// </summary>
    public static string TruncateFileContent(string fileContent, int? maxLength = null)
    {
        return TruncateIfNeeded(fileContent, FileTruncationHint, maxLength);
    }

    /// <summary>
    /// Truncates search results with appropriate hint.
    /// </summary>
    public static string TruncateSearchResults(string searchResults, int? maxLength = null)
    {
        return TruncateIfNeeded(searchResults, SearchTruncationHint, maxLength);
    }

    /// <summary>
    /// Clamps a user-provided limit to safe bounds.
    /// </summary>
    /// <param name="limit">The user-provided limit.</param>
    /// <param name="defaultLimit">Default if limit is 0 or negative.</param>
    /// <param name="maxLimit">Maximum allowed limit.</param>
    /// <returns>A clamped limit value.</returns>
    public static int ClampLimit(int limit, int defaultLimit = DefaultListLimit, int maxLimit = MaxListLimit)
    {
        if (limit <= 0)
        {
            return defaultLimit;
        }

        return Math.Min(limit, maxLimit);
    }

    /// <summary>
    /// Checks if content would need truncation.
    /// </summary>
    public static bool WouldTruncate(string content, int? maxLength = null)
    {
        var effectiveMaxLength = maxLength ?? MaxResponseLengthBytes;
        return content?.Length > effectiveMaxLength;
    }

    /// <summary>
    /// Calculates approximate remaining length after truncation hint is added.
    /// </summary>
    public static int GetAvailableLength(string hint, int? maxLength = null)
    {
        var effectiveMaxLength = maxLength ?? MaxResponseLengthBytes;
        return effectiveMaxLength - hint.Length;
    }
}