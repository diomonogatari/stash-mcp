using Bitbucket.Net.Common.Mcp;
using Bitbucket.Net.Models.Core.Projects;
using System.Text;

namespace StashMcpServer.Formatting;

public class DiffFormatter(ILogger<DiffFormatter> logger) : IDiffFormatter
{
    private const string NullDevicePath = "/dev/null";

    public Task<string> FormatDiffText(Differences diff, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        try
        {
            if (diff?.Diffs == null)
            {
                sb.AppendLine("No diff content returned.");
                logger.LogWarning("Diffs property is null in the Differences object.");
                return Task.FromResult(sb.ToString());
            }

            // Track if we're approaching the limit
            var maxLength = ResponseTruncation.GetAvailableLength(ResponseTruncation.DiffTruncationHint);

            // Format each file's diff
            foreach (var fileDiff in diff.Diffs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check if we're approaching truncation limit
                if (sb.Length > maxLength)
                {
                    logger.LogDebug("Diff output truncated at {Length} bytes", sb.Length);
                    break;
                }

                FormatSingleDiff(sb, fileDiff);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error formatting diff payload that contains {DiffCount} items.", diff?.Diffs?.Count);
            sb.AppendLine($"Error retrieving diff: {ex.Message}");
        }

        // Apply truncation if needed
        return Task.FromResult(ResponseTruncation.TruncateDiff(sb.ToString()));
    }

    /// <inheritdoc />
    public async Task<string> FormatDiffStreamAsync(
        IAsyncEnumerable<Diff> diffs,
        int maxLines = 2000,
        int maxFiles = 50,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        try
        {
            // Use the streaming extension to handle truncation mid-stream
            var result = await diffs.TakeDiffsWithLimitsAsync(
                maxLines: maxLines,
                maxFiles: maxFiles,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result.Diffs.Count == 0)
            {
                sb.AppendLine("No diff content returned.");
                return sb.ToString();
            }

            foreach (var fileDiff in result.Diffs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FormatSingleDiff(sb, fileDiff);
            }

            // Add truncation metadata if applicable
            if (result.WasTruncated)
            {
                sb.AppendLine();
                sb.AppendLine($"[Diff truncated: {result.TruncationReason}. Processed {result.TotalFiles} files, {result.TotalLines} lines.]");
                sb.AppendLine("[Use get_file_content for specific files or get_pull_request_changes to see the list of changed files.]");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error formatting streaming diff");
            sb.AppendLine($"Error retrieving diff: {ex.Message}");
        }

        return ResponseTruncation.TruncateDiff(sb.ToString());
    }

    private void FormatSingleDiff(StringBuilder sb, Diff fileDiff)
    {
        string sourcePath = fileDiff.Source?.toString ?? NullDevicePath;
        string destinationPath = fileDiff.Destination?.toString ?? NullDevicePath;

        string fileType;
        if (sourcePath == NullDevicePath)
        {
            fileType = "Added";
        }
        else if (destinationPath == NullDevicePath)
        {
            fileType = "Deleted";
        }
        else
        {
            fileType = "Modified";
        }

        var fileName = fileType == "Deleted" ? sourcePath : destinationPath;

        sb.AppendLine($"File: {fileName} ({fileType})");
        sb.AppendLine("------------------------------");

        // Check for binary files (heuristic: no hunks but file changed)
        if ((fileDiff.Hunks == null || !fileDiff.Hunks.Any()) && fileType != "Deleted" && fileType != "Added")
        {
            // If it's modified but has no hunks, it might be binary or just metadata change
            // We can't easily detect binary from this model without a specific flag, 
            // but usually binary files don't return hunks in text diff endpoints.
            sb.AppendLine("[Binary or large file - content not shown]");
            sb.AppendLine();
            return;
        }

        if (fileDiff.Hunks != null)
        {
            foreach (var hunk in fileDiff.Hunks)
            {
                sb.AppendLine($"@@ -{hunk.SourceLine},{hunk.SourceSpan} +{hunk.DestinationLine},{hunk.DestinationSpan} @@");

                foreach (var segment in hunk.Segments ?? [])
                {
                    foreach (var lineRef in segment.Lines ?? [])
                    {
                        char prefix = ' ';
                        if (segment.Type == "ADDED") prefix = '+';
                        else if (segment.Type == "REMOVED") prefix = '-';
                        else if (segment.Type == "CONTEXT") prefix = ' ';

                        sb.AppendLine($"{prefix}{lineRef.Line}");
                    }
                }
                sb.AppendLine();
            }
        }
        sb.AppendLine();
    }
}