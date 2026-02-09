using Bitbucket.Net.Models.Core.Projects;

namespace StashMcpServer.Formatting;

/// <summary>
/// Formats the Bitbucket diff response into readable text.
/// </summary>
public interface IDiffFormatter
{
    /// <summary>
    /// Formats a complete Differences object into readable text.
    /// </summary>
    /// <param name="diff">The diff object to format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A formatted diff string.</returns>
    Task<string> FormatDiffText(Differences diff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams and formats diffs with size limits for memory efficiency.
    /// Terminates early when size limit is reached.
    /// </summary>
    /// <param name="diffs">Async enumerable of diffs from streaming API.</param>
    /// <param name="maxLines">Maximum total diff lines to include. Default 2000.</param>
    /// <param name="maxFiles">Maximum number of files to include. Default 50.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A formatted diff string with truncation metadata if applicable.</returns>
    Task<string> FormatDiffStreamAsync(
        IAsyncEnumerable<Diff> diffs,
        int maxLines = 2000,
        int maxFiles = 50,
        CancellationToken cancellationToken = default);
}