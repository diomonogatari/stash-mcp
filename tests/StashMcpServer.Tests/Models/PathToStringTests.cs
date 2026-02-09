using BitbucketPath = Bitbucket.Net.Models.Core.Projects.Path;

namespace StashMcpServer.Tests.Models;

/// <summary>
/// Tests for Path model ToString() override, verifying BUG-004 fix
/// where get_commit_changes displayed the type name instead of the actual file path.
/// </summary>
public class PathToStringTests
{
    [Fact]
    public void ToString_ReturnsToStringProperty_WhenAvailable()
    {
        var path = new BitbucketPath
        {
            toString = "src/MyFile.cs",
            Components = ["src", "MyFile.cs"],
            Name = "MyFile.cs"
        };
        var result = path.ToString();
        Assert.Equal("src/MyFile.cs", result);
    }

    [Fact]
    public void ToString_ReturnsJoinedComponents_WhenToStringIsNull()
    {
        var path = new BitbucketPath
        {
            toString = null,
            Components = ["src", "services", "MyService.cs"],
            Name = "MyService.cs"
        };
        var result = path.ToString();
        Assert.Equal("src/services/MyService.cs", result);
    }

    [Fact]
    public void ToString_ReturnsJoinedComponents_WhenToStringIsEmpty()
    {
        var path = new BitbucketPath
        {
            toString = "",
            Components = ["abstract_docs", "Backlog.md"],
            Name = "Backlog.md"
        };
        var result = path.ToString();
        Assert.Equal("abstract_docs/Backlog.md", result);
    }

    [Fact]
    public void ToString_ReturnsName_WhenToStringAndComponentsAreEmpty()
    {
        var path = new BitbucketPath
        {
            toString = null,
            Components = null,
            Name = "README.md"
        };
        var result = path.ToString();
        Assert.Equal("README.md", result);
    }

    [Fact]
    public void ToString_ReturnsName_WhenComponentsIsEmptyList()
    {
        var path = new BitbucketPath
        {
            toString = null,
            Components = [],
            Name = "Config.json"
        };
        var result = path.ToString();
        Assert.Equal("Config.json", result);
    }

    [Fact]
    public void ToString_ReturnsFallback_WhenAllPropertiesAreNull()
    {
        var path = new BitbucketPath
        {
            toString = null,
            Components = null,
            Name = null
        };
        var result = path.ToString();
        Assert.Equal("(unknown path)", result);
    }

    [Fact]
    public void ToString_HandlesDeepNestedPath()
    {
        var path = new BitbucketPath
        {
            toString = "src/main/java/com/example/MyClass.java",
            Components = ["src", "main", "java", "com", "example", "MyClass.java"],
            Name = "MyClass.java"
        };
        var result = path.ToString();
        Assert.Equal("src/main/java/com/example/MyClass.java", result);
    }

    [Fact]
    public void ToString_DoesNotReturnTypeName()
    {
        var path = new BitbucketPath
        {
            toString = "abstract_docs/Backlog.md"
        };
        var result = path.ToString();
        Assert.DoesNotContain("Bitbucket.Net.Models.Core.Projects.Path", result);
        Assert.Equal("abstract_docs/Backlog.md", result);
    }
}