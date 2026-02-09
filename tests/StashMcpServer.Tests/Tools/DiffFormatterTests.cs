using Bitbucket.Net.Models.Core.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using StashMcpServer.Formatting;
using Path = Bitbucket.Net.Models.Core.Projects.Path;

namespace StashMcpServer.Tests.Tools;

public class DiffFormatterTests
{
    private readonly DiffFormatter _formatter;

    public DiffFormatterTests()
    {
        _formatter = new DiffFormatter(NullLogger<DiffFormatter>.Instance);
    }

    [Fact]
    public async Task FormatDiffText_WithNullDiff_ReturnsNoContentMessage()
    {
        Differences? diff = null;
        var result = await _formatter.FormatDiffText(diff!);
        Assert.Contains("No diff content returned", result);
    }

    [Fact]
    public async Task FormatDiffText_WithEmptyDiffs_ReturnsNoContentMessage()
    {
        var diff = new Differences { Diffs = [] };
        var result = await _formatter.FormatDiffText(diff);
        Assert.DoesNotContain("truncated", result.ToLower());
    }

    [Fact]
    public async Task FormatDiffText_WithValidDiff_FormatsCorrectly()
    {
        var diff = new Differences
        {
            Diffs =
            [
                new Diff
                {
                    Source = new Path { toString = "file.txt" },
                    Destination = new Path { toString = "file.txt" },
                    Hunks =
                    [
                        new DiffHunk
                        {
                            SourceLine = 1,
                            SourceSpan = 3,
                            DestinationLine = 1,
                            DestinationSpan = 4,
                            Segments =
                            [
                                new Segment
                                {
                                    Type = "CONTEXT",
                                    Lines = [new LineRef { Line = "context line" }]
                                },
                                new Segment
                                {
                                    Type = "REMOVED",
                                    Lines = [new LineRef { Line = "removed line" }]
                                },
                                new Segment
                                {
                                    Type = "ADDED",
                                    Lines = [new LineRef { Line = "added line 1" }, new LineRef { Line = "added line 2" }]
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        var result = await _formatter.FormatDiffText(diff);
        Assert.Contains("file.txt", result);
        Assert.Contains("Modified", result);
        Assert.Contains("@@ -1,3 +1,4 @@", result);
        Assert.Contains(" context line", result);
        Assert.Contains("-removed line", result);
        Assert.Contains("+added line 1", result);
        Assert.Contains("+added line 2", result);
    }

    [Fact]
    public async Task FormatDiffText_WithAddedFile_ShowsAddedType()
    {
        var diff = new Differences
        {
            Diffs =
            [
                new Diff
                {
                    Source = null,  // Source is null for new files
                    Destination = new Path { toString = "newfile.txt" },
                    Hunks =
                    [
                        new DiffHunk
                        {
                            SourceLine = 0,
                            SourceSpan = 0,
                            DestinationLine = 1,
                            DestinationSpan = 1,
                            Segments =
                            [
                                new Segment
                                {
                                    Type = "ADDED",
                                    Lines = [new LineRef { Line = "new content" }]
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        var result = await _formatter.FormatDiffText(diff);
        Assert.Contains("newfile.txt", result);
        Assert.Contains("Added", result);
    }

    [Fact]
    public async Task FormatDiffText_WithDeletedFile_ShowsDeletedType()
    {
        var diff = new Differences
        {
            Diffs =
            [
                new Diff
                {
                    Source = new Path { toString = "deleted.txt" },
                    Destination = null,  // Destination is null for deleted files
                    Hunks = []
                }
            ]
        };
        var result = await _formatter.FormatDiffText(diff);
        Assert.Contains("deleted.txt", result);
        Assert.Contains("Deleted", result);
    }

    [Fact]
    public async Task FormatDiffStreamAsync_WithEmptyStream_ReturnsNoContentMessage()
    {
        var emptyStream = AsyncEnumerable.Empty<Diff>();
        var result = await _formatter.FormatDiffStreamAsync(emptyStream);
        Assert.Contains("No diff content returned", result);
    }

    [Fact]
    public async Task FormatDiffStreamAsync_WithValidDiffs_FormatsCorrectly()
    {
        var diffs = CreateTestDiffStream();
        var result = await _formatter.FormatDiffStreamAsync(diffs);
        Assert.Contains("file1.txt", result);
        Assert.Contains("file2.txt", result);
        Assert.Contains("+added line", result);
        Assert.Contains("-removed line", result);
    }

    [Fact]
    public async Task FormatDiffStreamAsync_WithMaxFilesLimit_TruncatesOutput()
    {
        var diffs = CreateManyFileDiffStream(count: 10);
        var result = await _formatter.FormatDiffStreamAsync(diffs, maxFiles: 3);
        Assert.Contains("truncated", result.ToLower());
        Assert.Contains("max_files_reached", result.ToLower());
    }

    [Fact]
    public async Task FormatDiffStreamAsync_WithMaxLinesLimit_TruncatesOutput()
    {
        var diffs = CreateLargeDiffStream(linesPerFile: 100);
        var result = await _formatter.FormatDiffStreamAsync(diffs, maxLines: 50);
        Assert.Contains("truncated", result.ToLower());
        Assert.Contains("max_lines_reached", result.ToLower());
    }

    [Fact]
    public async Task FormatDiffStreamAsync_RespectsСancellation()
    {
        var cts = new CancellationTokenSource();
        var diffs = CreateManyFileDiffStream(count: 100);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _formatter.FormatDiffStreamAsync(diffs, cancellationToken: cts.Token));
    }

    private static async IAsyncEnumerable<Diff> CreateTestDiffStream()
    {
        yield return new Diff
        {
            Source = new Path { toString = "file1.txt" },
            Destination = new Path { toString = "file1.txt" },
            Hunks =
            [
                new DiffHunk
                {
                    SourceLine = 1,
                    SourceSpan = 1,
                    DestinationLine = 1,
                    DestinationSpan = 1,
                    Segments =
                    [
                        new Segment
                        {
                            Type = "ADDED",
                            Lines = [new LineRef { Line = "added line" }]
                        }
                    ]
                }
            ]
        };

        yield return new Diff
        {
            Source = new Path { toString = "file2.txt" },
            Destination = new Path { toString = "file2.txt" },
            Hunks =
            [
                new DiffHunk
                {
                    SourceLine = 1,
                    SourceSpan = 1,
                    DestinationLine = 1,
                    DestinationSpan = 0,
                    Segments =
                    [
                        new Segment
                        {
                            Type = "REMOVED",
                            Lines = [new LineRef { Line = "removed line" }]
                        }
                    ]
                }
            ]
        };

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Diff> CreateManyFileDiffStream(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new Diff
            {
                Source = new Path { toString = $"file{i}.txt" },
                Destination = new Path { toString = $"file{i}.txt" },
                Hunks =
                [
                    new DiffHunk
                    {
                        SourceLine = 1,
                        SourceSpan = 1,
                        DestinationLine = 1,
                        DestinationSpan = 1,
                        Segments =
                        [
                            new Segment
                            {
                                Type = "ADDED",
                                Lines = [new LineRef { Line = $"content {i}" }]
                            }
                        ]
                    }
                ]
            };
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Diff> CreateLargeDiffStream(int linesPerFile)
    {
        var lines = new List<LineRef>();
        for (int i = 0; i < linesPerFile; i++)
        {
            lines.Add(new LineRef { Line = $"line {i}" });
        }

        yield return new Diff
        {
            Source = new Path { toString = "largefile.txt" },
            Destination = new Path { toString = "largefile.txt" },
            Hunks =
            [
                new DiffHunk
                {
                    SourceLine = 1,
                    SourceSpan = 0,
                    DestinationLine = 1,
                    DestinationSpan = linesPerFile,
                    Segments =
                    [
                        new Segment
                        {
                            Type = "ADDED",
                            Lines = lines
                        }
                    ]
                }
            ]
        };

        await Task.CompletedTask;
    }
}