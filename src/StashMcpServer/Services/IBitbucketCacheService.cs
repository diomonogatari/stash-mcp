using Bitbucket.Net.Models.Core.Projects;
using Bitbucket.Net.Models.Core.Users;

namespace StashMcpServer.Services;

/// <summary>
/// Provides cached access to Bitbucket project, repository, user, and branch data.
/// </summary>
public interface IBitbucketCacheService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // Projects and Repositories
    IEnumerable<Project> GetProjects();
    IEnumerable<Repository> GetRepositories(string projectKey);
    IEnumerable<Repository> GetAllRepositories();
    Project? FindProject(string projectKey);
    Repository? FindRepository(string projectKey, string repositorySlug);
    void StoreRepositories(string projectKey, IEnumerable<Repository> repositories);

    // User Context
    User? GetCurrentUser();
    string? GetCurrentUserSlug();
    string? GetCurrentUserDisplayName();
    IEnumerable<Repository> GetRecentRepositories();
    bool IsCurrentUser(string? userSlugOrName);

    // Default Branches (lazy-loaded: fetched on demand if not cached)
    ValueTask<Branch?> GetDefaultBranchAsync(string projectKey, string repositorySlug, CancellationToken cancellationToken = default);
    ValueTask<string?> GetDefaultBranchNameAsync(string projectKey, string repositorySlug, CancellationToken cancellationToken = default);
    ValueTask<string> GetDefaultBranchRefAsync(string projectKey, string repositorySlug, string fallback = "refs/heads/master", CancellationToken cancellationToken = default);
    void StoreDefaultBranch(string projectKey, string repositorySlug, Branch branch);
    Task WarmupDefaultBranchesAsync(CancellationToken cancellationToken = default);

    // Application Properties
    IDictionary<string, object?>? GetApplicationProperties();
    string? GetServerVersion();
    string? GetServerDisplayName();
    string? GetBuildNumber();

    // Server Capabilities
    bool IsSearchAvailable();
}