using Bitbucket.Net.Common.Models.Search;
using Bitbucket.Net.Serialization;
using System.Text.Json;

namespace StashMcpServer.Tests.Models;

public class CodeSearchSerializationTests
{
    private readonly JsonSerializerOptions _options = BitbucketJsonContext.Default.Options;

    [Fact]
    public void CodeSearchRequest_Serializes_Correctly()
    {
        var request = new CodeSearchRequest
        {
            Query = "await nextHandler",
            Entities = SearchEntities.CodeOnly,
            Limits = new SearchLimits { Primary = 25, Secondary = 10 }
        };

        var json = JsonSerializer.Serialize(request, _options);

        Assert.Contains("\"query\":\"await nextHandler\"", json);
        Assert.Contains("\"primary\":25", json);
        Assert.Contains("\"secondary\":10", json);
        Assert.Contains("\"code\":{}", json);
    }

    [Fact]
    public void CodeSearchResponse_Deserializes_Full_Response()
    {
        var json = """
        {
            "scope": { "type": "GLOBAL" },
            "code": {
                "category": "primary",
                "isLastPage": false,
                "count": 61,
                "start": 0,
                "nextStart": 25,
                "values": [
                    {
                        "repository": {
                            "slug": "my-repo",
                            "id": 123,
                            "name": "My Repo",
                            "project": { "key": "PROJ", "name": "Project" }
                        },
                        "file": "src/Middleware.cs",
                        "hitContexts": [
                            [
                                { "line": 10, "text": "    var result = <em>await</em> <em>nextHandler</em>();" },
                                { "line": 11, "text": "    return result;" }
                            ]
                        ],
                        "pathMatches": [],
                        "hitCount": 3
                    }
                ]
            },
            "query": { "substituted": false }
        }
        """;

        var result = JsonSerializer.Deserialize<CodeSearchResponse>(json, _options);

        Assert.NotNull(result);
        Assert.NotNull(result.Scope);
        Assert.Equal("GLOBAL", result.Scope.Type);
        Assert.NotNull(result.Code);
        Assert.Equal(61, result.Code.Count);
        Assert.False(result.Code.IsLastPage);
        Assert.Equal(25, result.Code.NextStart);
        Assert.Single(result.Code.Values);

        var hit = result.Code.Values[0];
        Assert.Equal("my-repo", hit.Repository?.Slug);
        Assert.Equal("src/Middleware.cs", hit.File);
        Assert.Equal(3, hit.HitCount);
        Assert.NotNull(hit.HitContexts);
        Assert.Single(hit.HitContexts);
        Assert.Equal(2, hit.HitContexts[0].Count);
        Assert.Equal(10, hit.HitContexts[0][0].Line);
        Assert.Contains("<em>await</em>", hit.HitContexts[0][0].Text);
    }

    [Fact]
    public void CodeSearchResponse_Deserializes_Empty_Results()
    {
        var json = """
        {
            "scope": { "type": "GLOBAL" },
            "code": {
                "category": "primary",
                "isLastPage": true,
                "count": 0,
                "start": 0,
                "values": []
            },
            "query": { "substituted": false }
        }
        """;

        var result = JsonSerializer.Deserialize<CodeSearchResponse>(json, _options);

        Assert.NotNull(result);
        Assert.NotNull(result.Code);
        Assert.Equal(0, result.Code.Count);
        Assert.True(result.Code.IsLastPage);
        Assert.Empty(result.Code.Values);
    }

    [Fact]
    public void CodeSearchResponse_Deserializes_Multiple_HitContexts()
    {
        var json = """
        {
            "scope": { "type": "GLOBAL" },
            "code": {
                "category": "primary",
                "isLastPage": true,
                "count": 1,
                "start": 0,
                "values": [
                    {
                        "repository": { "slug": "repo", "project": { "key": "PROJ" } },
                        "file": "test.cs",
                        "hitContexts": [
                            [
                                { "line": 5, "text": "context before" },
                                { "line": 6, "text": "<em>match</em> here" },
                                { "line": 7, "text": "context after" }
                            ],
                            [
                                { "line": 20, "text": "another <em>match</em>" }
                            ]
                        ],
                        "pathMatches": [{ "start": 0, "length": 4 }],
                        "hitCount": 2
                    }
                ]
            },
            "query": { "substituted": false }
        }
        """;

        var result = JsonSerializer.Deserialize<CodeSearchResponse>(json, _options);

        Assert.NotNull(result);
        Assert.NotNull(result.Code);
        Assert.NotNull(result.Code.Values);

        var hit = Assert.Single(result.Code.Values);

        Assert.NotNull(hit.HitContexts);
        Assert.Equal(2, hit.HitContexts.Count);
        Assert.Equal(3, hit.HitContexts[0].Count);
        Assert.Single(hit.HitContexts[1]);

        Assert.NotNull(hit.PathMatches);
        var pathMatch = Assert.Single(hit.PathMatches);
        Assert.Equal(0, pathMatch.Start);
        Assert.Equal(4, pathMatch.Length);
    }

    [Fact]
    public void SearchEntities_CodeOnly_Creates_Empty_Code_Object()
    {
        var entities = SearchEntities.CodeOnly;
        Assert.NotNull(entities.Code);

        var json = JsonSerializer.Serialize(entities, _options);
        Assert.Contains("\"code\":{}", json);
    }

    [Fact]
    public void SearchLimits_Defaults_Are_Correct()
    {
        var limits = new SearchLimits();
        Assert.Equal(25, limits.Primary);
        Assert.Equal(10, limits.Secondary);
    }
}