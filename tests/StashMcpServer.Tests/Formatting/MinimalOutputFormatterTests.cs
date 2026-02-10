using Bitbucket.Net.Models.Core.Projects;
using Bitbucket.Net.Models.Core.Users;
using StashMcpServer.Formatting;

namespace StashMcpServer.Tests.Formatting;

public class MinimalOutputFormatterTests
{
    [Fact]
    public void FormatRepositories_EmptyList_ReturnsHeaderOnly()
    {
        var result = MinimalOutputFormatter.FormatRepositories([], "PROJ");

        Assert.Contains("Repositories in PROJ:", result);
        Assert.DoesNotContain("- ", result);
    }

    [Fact]
    public void FormatRepositories_WithRepos_ReturnsSlugAndName()
    {
        var repos = new[]
        {
            new Repository { Slug = "my-repo", Name = "My Repo" },
            new Repository { Slug = "other", Name = null },
        };

        var result = MinimalOutputFormatter.FormatRepositories(repos, "PROJ");

        Assert.Contains("- my-repo: My Repo", result);
        Assert.Contains("- other: other", result);
    }

    [Fact]
    public void FormatPullRequests_EmptyList_ReturnsHeader()
    {
        var result = MinimalOutputFormatter.FormatPullRequests([], "PROJ", "repo", "OPEN");

        Assert.Contains("PRs [OPEN] PROJ/repo:", result);
    }

    [Fact]
    public void FormatPullRequests_WithPRs_FormatsStateAndAuthor()
    {
        var prs = new[]
        {
            new PullRequest
            {
                Id = 42,
                Title = "Fix bug",
                State = PullRequestStates.Open,
                Author = new Participant { User = new User { Name = "jdoe" } },
            },
            new PullRequest
            {
                Id = 99,
                Title = "Add feature",
                State = PullRequestStates.Merged,
                Author = null,
            },
        };

        var result = MinimalOutputFormatter.FormatPullRequests(prs, "PROJ", "repo", "ALL");

        Assert.Contains("- #42: Fix bug [O] @jdoe", result);
        Assert.Contains("- #99: Add feature [M] @?", result);
    }

    [Fact]
    public void FormatPullRequests_DeclinedState_ShowsD()
    {
        var prs = new[]
        {
            new PullRequest
            {
                Id = 1,
                Title = "Bad PR",
                State = PullRequestStates.Declined,
                Author = new Participant { User = new User { Name = "bob" } },
            },
        };

        var result = MinimalOutputFormatter.FormatPullRequests(prs, "P", "r", "ALL");

        Assert.Contains("[D]", result);
    }

    [Fact]
    public void FormatBranches_EmptyList_ReturnsHeader()
    {
        var result = MinimalOutputFormatter.FormatBranches([], "PROJ", "repo");

        Assert.Contains("Branches in PROJ/repo:", result);
    }

    [Fact]
    public void FormatBranches_WithBranches_ReturnsDisplayId()
    {
        var branches = new[]
        {
            new Branch { DisplayId = "main", IsDefault = true },
            new Branch { DisplayId = "develop", IsDefault = false },
        };

        var result = MinimalOutputFormatter.FormatBranches(branches, "PROJ", "repo");

        Assert.Contains("- main", result);
        Assert.Contains("- develop", result);
    }

    [Fact]
    public void FormatBranches_ShowDefault_MarksDefaultBranch()
    {
        var branches = new[]
        {
            new Branch { DisplayId = "main", IsDefault = true },
            new Branch { DisplayId = "develop", IsDefault = false },
        };

        var result = MinimalOutputFormatter.FormatBranches(branches, "PROJ", "repo", showDefault: true);

        Assert.Contains("- main *", result);
        Assert.DoesNotContain("- develop *", result);
    }

    [Fact]
    public void FormatTags_EmptyList_ReturnsHeader()
    {
        var result = MinimalOutputFormatter.FormatTags([], "PROJ", "repo");

        Assert.Contains("Tags in PROJ/repo:", result);
    }

    [Fact]
    public void FormatTags_WithTags_ReturnsDisplayId()
    {
        var tags = new[]
        {
            new Tag { DisplayId = "v1.0.0" },
            new Tag { DisplayId = "v2.0.0" },
        };

        var result = MinimalOutputFormatter.FormatTags(tags, "PROJ", "repo");

        Assert.Contains("- v1.0.0", result);
        Assert.Contains("- v2.0.0", result);
    }

    [Fact]
    public void FormatFiles_IncludesTotalCountAndRef()
    {
        var files = new[] { "src/Program.cs", "README.md" };

        var result = MinimalOutputFormatter.FormatFiles(files, "PROJ", "repo", "main", 2);

        Assert.Contains("Files in PROJ/repo@main (2 total):", result);
        Assert.Contains("src/Program.cs", result);
        Assert.Contains("README.md", result);
    }

    [Fact]
    public void FormatFiles_NullRef_OmitsRefInfo()
    {
        var result = MinimalOutputFormatter.FormatFiles(["file.txt"], "PROJ", "repo", null, 1);

        Assert.Contains("Files in PROJ/repo (1 total):", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact]
    public void FormatProjects_WithProjects_FormatsKeyAndName()
    {
        var projects = new[]
        {
            new Project { Key = "PROJ", Name = "My Project" },
            new Project { Key = "TEAM", Name = "Team Project" },
        };

        var result = MinimalOutputFormatter.FormatProjects(projects);

        Assert.Contains("Projects:", result);
        Assert.Contains("- PROJ: My Project", result);
        Assert.Contains("- TEAM: Team Project", result);
    }

    [Fact]
    public void FormatCommits_WithRepoSlug_FormatsScope()
    {
        var commits = new[]
        {
            new Commit { DisplayId = "abc1234", Message = "Fix bug" },
        };

        var result = MinimalOutputFormatter.FormatCommits(commits, "PROJ", "repo");

        Assert.Contains("Commits in PROJ/repo:", result);
        Assert.Contains("- abc1234: Fix bug", result);
    }

    [Fact]
    public void FormatCommits_WithoutRepoSlug_UsesProjectOnly()
    {
        var commits = new[]
        {
            new Commit { DisplayId = "abc1234", Message = "Fix" },
        };

        var result = MinimalOutputFormatter.FormatCommits(commits, "PROJ", null);

        Assert.Contains("Commits in PROJ:", result);
    }

    [Fact]
    public void FormatCommits_LongMessage_TruncatesAt60Chars()
    {
        var longMessage = new string('A', 80);
        var commits = new[]
        {
            new Commit { DisplayId = "abc1234", Message = longMessage },
        };

        var result = MinimalOutputFormatter.FormatCommits(commits, "PROJ", "repo");

        Assert.Contains("...", result);
        Assert.DoesNotContain(longMessage, result);
    }

    [Fact]
    public void FormatCommits_NullDisplayId_FallsBackToIdPrefix()
    {
        var commits = new[]
        {
            new Commit { Id = "abcdef1234567890", DisplayId = null, Message = "test" },
        };

        var result = MinimalOutputFormatter.FormatCommits(commits, "PROJ", "repo");

        Assert.Contains("- abcdef1: test", result);
    }

    [Fact]
    public void FormatCommits_MultiLineMessage_UsesFirstLine()
    {
        var commits = new[]
        {
            new Commit { DisplayId = "abc1234", Message = "First line\nSecond line\nThird line" },
        };

        var result = MinimalOutputFormatter.FormatCommits(commits, "PROJ", "repo");

        Assert.Contains("- abc1234: First line", result);
        Assert.DoesNotContain("Second line", result);
    }
}
