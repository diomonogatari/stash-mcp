using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace StashMcpServer.SystemTests.Infrastructure;

/// <summary>
/// Populates a freshly-booted Bitbucket instance with a deterministic fixture — a project, a repo
/// with history on two branches, a tag, an open pull request with a comment and a task, and a build
/// status — entirely through the REST API (no git binary required). Also mints the HTTP access
/// token the MCP server authenticates with.
/// </summary>
internal sealed class BitbucketSeeder(Uri baseUrl)
{
    private const string ProjectKey = "STASH";
    private const string ProjectName = "Stash MCP Demo";
    private const string RepositorySlug = "demo-repo";
    private const string DefaultBranch = "main";
    private const string FeatureBranch = "feature/demo";
    private const string TagName = "v1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Runs the full seeding sequence. The first call (token creation) is retried briefly because
    /// the access-tokens plugin can lag a few seconds behind the <c>/status=RUNNING</c> signal.
    /// </summary>
    public async Task<SeededData> SeedAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = baseUrl };
        SetBasicAuth(http, BitbucketContainer.AdminUsername, BitbucketContainer.AdminPassword);

        var token = await CreateAccessTokenWithRetryAsync(http, cancellationToken).ConfigureAwait(false);

        await CreateProjectAsync(http, cancellationToken).ConfigureAwait(false);
        await CreateRepositoryAsync(http, cancellationToken).ConfigureAwait(false);

        // Two commits on the default branch establish history; the first one creates the branch.
        var firstCommit = await PutFileAsync(http, "README.md", DefaultBranch, sourceCommitId: null,
            "# Demo\n\nSeeded by the stash-mcp end-to-end harness.\n", "Add README", cancellationToken).ConfigureAwait(false);
        await TrySetDefaultBranchAsync(http, cancellationToken).ConfigureAwait(false);
        var headCommit = await PutFileAsync(http, "README.md", DefaultBranch, sourceCommitId: firstCommit,
            "# Demo\n\nSeeded by the stash-mcp end-to-end harness.\n\nSecond commit.\n", "Expand README", cancellationToken).ConfigureAwait(false);

        await CreateTagAsync(http, headCommit, cancellationToken).ConfigureAwait(false);

        // A feature branch with its own change gives the pull request a non-empty diff.
        await CreateBranchAsync(http, FeatureBranch, headCommit, cancellationToken).ConfigureAwait(false);
        await PutFileAsync(http, "feature.txt", FeatureBranch, sourceCommitId: null,
            "feature work in progress\n", "Add feature.txt", cancellationToken).ConfigureAwait(false);

        var pullRequest = await CreatePullRequestAsync(http, cancellationToken).ConfigureAwait(false);
        await AddCommentAsync(http, pullRequest.Id, cancellationToken).ConfigureAwait(false);
        await AddTaskAsync(http, pullRequest.Id, cancellationToken).ConfigureAwait(false);

        await AddBuildStatusAsync(http, headCommit, cancellationToken).ConfigureAwait(false);

        return new SeededData
        {
            AccessToken = token,
            ProjectKey = ProjectKey,
            ProjectName = ProjectName,
            RepositorySlug = RepositorySlug,
            DefaultBranch = DefaultBranch,
            FeatureBranch = FeatureBranch,
            PullRequestId = pullRequest.Id,
            PullRequestTitle = pullRequest.Title,
            HeadCommitId = headCommit,
            TagName = TagName,
        };
    }

    private async Task<string> CreateAccessTokenWithRetryAsync(HttpClient http, CancellationToken cancellationToken)
    {
        const int maxAttempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CreateAccessTokenAsync(http, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> CreateAccessTokenAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var payload = new { name = "stash-mcp-e2e", permissions = new[] { "PROJECT_ADMIN", "REPO_ADMIN" } };
        var json = await SendJsonAsync(http, HttpMethod.Put,
            $"rest/access-tokens/latest/users/{BitbucketContainer.AdminUsername}", payload, cancellationToken).ConfigureAwait(false);
        return json.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Access token response did not contain a token.");
    }

    private static async Task CreateProjectAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var payload = new { key = ProjectKey, name = ProjectName, description = "Seeded by the stash-mcp E2E harness." };
        await SendJsonAsync(http, HttpMethod.Post, "rest/api/latest/projects", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateRepositoryAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var payload = new { name = RepositorySlug, scmId = "git" };
        await SendJsonAsync(http, HttpMethod.Post,
            $"rest/api/latest/projects/{ProjectKey}/repos", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task TrySetDefaultBranchAsync(HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { id = $"refs/heads/{DefaultBranch}" };
            await SendJsonAsync(http, HttpMethod.Put,
                $"rest/api/latest/projects/{ProjectKey}/repos/{RepositorySlug}/default-branch", payload, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Best-effort: the first commit already makes 'main' the default on current Bitbucket.
        }
    }

    private static async Task<string> PutFileAsync(
        HttpClient http, string path, string branch, string? sourceCommitId, string content, string message, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(content), "content" },
            { new StringContent(message), "message" },
            { new StringContent(branch), "branch" },
        };
        if (sourceCommitId is not null)
        {
            form.Add(new StringContent(sourceCommitId), "sourceCommitId");
        }

        using var response = await http.PutAsync(
            new Uri($"rest/api/latest/projects/{ProjectKey}/repos/{RepositorySlug}/browse/{path}", UriKind.Relative),
            form, cancellationToken).ConfigureAwait(false);
        var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return json.GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"File commit for '{path}' did not return a commit id.");
    }

    private static async Task CreateBranchAsync(HttpClient http, string name, string startCommitId, CancellationToken cancellationToken)
    {
        var payload = new { name, startPoint = startCommitId };
        await SendJsonAsync(http, HttpMethod.Post,
            $"rest/branch-utils/latest/projects/{ProjectKey}/repos/{RepositorySlug}/branches", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateTagAsync(HttpClient http, string startCommitId, CancellationToken cancellationToken)
    {
        var payload = new { name = TagName, startPoint = startCommitId, message = "Release 1.0.0" };
        await SendJsonAsync(http, HttpMethod.Post,
            $"rest/git/latest/projects/{ProjectKey}/repos/{RepositorySlug}/tags", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(long Id, string Title)> CreatePullRequestAsync(HttpClient http, CancellationToken cancellationToken)
    {
        const string title = "Add feature.txt";
        var repository = new { slug = RepositorySlug, project = new { key = ProjectKey } };
        var payload = new
        {
            title,
            description = "Seeded demo pull request.",
            fromRef = new { id = $"refs/heads/{FeatureBranch}", repository },
            toRef = new { id = $"refs/heads/{DefaultBranch}", repository },
        };
        var json = await SendJsonAsync(http, HttpMethod.Post,
            $"rest/api/latest/projects/{ProjectKey}/repos/{RepositorySlug}/pull-requests", payload, cancellationToken).ConfigureAwait(false);
        return (json.GetProperty("id").GetInt64(), title);
    }

    private static async Task AddCommentAsync(HttpClient http, long pullRequestId, CancellationToken cancellationToken)
    {
        var payload = new { text = "Nice work — one nit noted as a task." };
        await SendJsonAsync(http, HttpMethod.Post,
            $"rest/api/latest/projects/{ProjectKey}/repos/{RepositorySlug}/pull-requests/{pullRequestId}/comments", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddTaskAsync(HttpClient http, long pullRequestId, CancellationToken cancellationToken)
    {
        // Modern Bitbucket models a task as a BLOCKER-severity comment.
        var payload = new { text = "Please rename feature.txt before merging.", severity = "BLOCKER" };
        await SendJsonAsync(http, HttpMethod.Post,
            $"rest/api/latest/projects/{ProjectKey}/repos/{RepositorySlug}/pull-requests/{pullRequestId}/blocker-comments", payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddBuildStatusAsync(HttpClient http, string commitId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            state = "SUCCESSFUL",
            key = "stash-mcp-ci",
            name = "CI",
            url = "https://example.com/ci/1",
            description = "Seeded build status.",
        };
        await SendJsonAsync(http, HttpMethod.Post,
            $"rest/build-status/latest/commits/{commitId}", payload, cancellationToken).ConfigureAwait(false);
    }

    private static void SetBasicAuth(HttpClient http, string username, string password)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private static async Task<JsonElement> SendJsonAsync<TPayload>(
        HttpClient http, HttpMethod method, string relativeUrl, TPayload payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(relativeUrl, UriKind.Relative))
        {
            // Generic so the anonymous payload's properties are serialized (an object-typed
            // parameter would serialize as an empty object).
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} for {response.RequestMessage?.RequestUri}: {Truncate(body)}",
                inner: null, response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500];
}