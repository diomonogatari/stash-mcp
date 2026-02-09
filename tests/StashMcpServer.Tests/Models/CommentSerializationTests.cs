using Bitbucket.Net.Models.Core.Projects;
using Bitbucket.Net.Serialization;
using System.Text.Json;

namespace StashMcpServer.Tests.Models;

public class CommentSerializationTests
{
    private readonly JsonSerializerOptions _options = BitbucketJsonContext.Default.Options;

    [Fact]
    public void Comment_Deserializes_State_And_ThreadResolved()
    {
        var json = """
        {
            "id": 123,
            "version": 1,
            "text": "Looks good",
            "state": "RESOLVED",
            "threadResolved": true,
            "createdDate": 1700000000000,
            "updatedDate": 1700000000000,
            "resolvedDate": 1700000000000,
            "author": { "name": "reviewer" },
            "resolver": { "name": "author" },
            "comments": []
        }
        """;

        var result = JsonSerializer.Deserialize<Comment>(json, _options);

        Assert.NotNull(result);
        Assert.Equal(123, result.Id);
        Assert.Equal("RESOLVED", result.State);
        Assert.True(result.ThreadResolved);
        Assert.NotNull(result.Resolver);
        Assert.NotNull(result.ResolvedDate);
    }

    [Fact]
    public void Comment_Deserializes_Open_State_When_Not_Resolved()
    {
        var json = """
        {
            "id": 456,
            "version": 1,
            "text": "Please rename this",
            "state": "OPEN",
            "threadResolved": false,
            "createdDate": 1700000000000,
            "author": { "name": "reviewer" },
            "comments": [{
                "id": 457,
                "version": 1,
                "text": "Ack",
                "state": "OPEN",
                "createdDate": 1700000000000,
                "author": { "name": "author" },
                "comments": []
            }]
        }
        """;

        var result = JsonSerializer.Deserialize<Comment>(json, _options);

        Assert.NotNull(result);
        Assert.Equal("OPEN", result.State);
        Assert.False(result.ThreadResolved);
        Assert.NotNull(result.Comments);
        Assert.Single(result.Comments);
        Assert.Equal("OPEN", result.Comments[0].State);
    }

    [Fact]
    public void Comment_ThreadResolved_IsNull_When_Field_Missing()
    {
        var json = """
        {
            "id": 789,
            "version": 1,
            "text": "No threadResolved present",
            "state": "PENDING",
            "createdDate": 1700000000000,
            "author": { "name": "reviewer" },
            "comments": []
        }
        """;

        var result = JsonSerializer.Deserialize<Comment>(json, _options);

        Assert.NotNull(result);
        Assert.Equal("PENDING", result.State);
        Assert.Null(result.ThreadResolved);
    }
}