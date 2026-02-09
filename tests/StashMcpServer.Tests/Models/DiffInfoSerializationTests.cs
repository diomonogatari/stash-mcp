using Bitbucket.Net.Models.Core.Projects;
using Bitbucket.Net.Serialization;
using System.Text.Json;

namespace StashMcpServer.Tests.Models;

/// <summary>
/// Tests for DiffInfo deserialization, specifically verifying BUG-001 fix
/// where Bitbucket Server 9.0 returns `truncated: true` (boolean) instead of string.
/// </summary>
public class DiffInfoSerializationTests
{
    private readonly JsonSerializerOptions _options = BitbucketJsonContext.Default.Options;

    [Fact]
    public void DiffInfo_Truncated_DeserializesFromBooleanTrue()
    {
        var json = """
        {
            "diffs": [{
                "source": { "toString": "src/file.cs" },
                "destination": { "toString": "src/file.cs" },
                "hunks": []
            }],
            "truncated": true,
            "contextLines": "3",
            "fromHash": "abc123",
            "toHash": "def456",
            "whiteSpace": "SHOW"
        }
        """;
        var result = JsonSerializer.Deserialize<Differences>(json, _options);
        Assert.NotNull(result);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void DiffInfo_Truncated_DeserializesFromBooleanFalse()
    {
        var json = """
        {
            "diffs": [],
            "truncated": false,
            "contextLines": "3",
            "fromHash": "abc123",
            "toHash": "def456"
        }
        """;
        var result = JsonSerializer.Deserialize<Differences>(json, _options);
        Assert.NotNull(result);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void DiffInfo_Truncated_DefaultsToFalseWhenMissing()
    {
        var json = """
        {
            "diffs": [],
            "fromHash": "abc123",
            "toHash": "def456"
        }
        """;
        var result = JsonSerializer.Deserialize<Differences>(json, _options);
        Assert.NotNull(result);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Diff_WithNestedTruncatedFields_DeserializesCorrectly()
    {
        var json = """
        {
            "diffs": [{
                "source": { "toString": "src/old.cs" },
                "destination": { "toString": "src/new.cs" },
                "hunks": [{
                    "sourceLine": 1,
                    "sourceSpan": 10,
                    "destinationLine": 1,
                    "destinationSpan": 12,
                    "truncated": true,
                    "segments": [{
                        "type": "ADDED",
                        "truncated": true,
                        "lines": [{
                            "destination": 1,
                            "source": 0,
                            "line": "+ new line",
                            "truncated": false
                        }]
                    }]
                }]
            }],
            "truncated": true,
            "contextLines": "3",
            "fromHash": "abc123",
            "toHash": "def456"
        }
        """;
        var result = JsonSerializer.Deserialize<Differences>(json, _options);
        Assert.NotNull(result);
        Assert.True(result.Truncated);
        Assert.Single(result.Diffs);

        var diff = result.Diffs[0];
        Assert.Single(diff.Hunks);
        Assert.True(diff.Hunks[0].Truncated);
        Assert.Single(diff.Hunks[0].Segments);
        Assert.True(diff.Hunks[0].Segments[0].Truncated);
        Assert.Single(diff.Hunks[0].Segments[0].Lines);
        Assert.False(diff.Hunks[0].Segments[0].Lines[0].Truncated);
    }
}