using Bitbucket.Net;
using Bitbucket.Net.Models.Core.Projects;
using Bitbucket.Net.Models.Core.Users;
using System.Collections.Concurrent;

namespace StashMcpServer.Services;

public class BitbucketCacheService(IBitbucketClient client, IServerSettings serverSettings, ILogger<BitbucketCacheService> logger) : IBitbucketCacheService
{
    private readonly IBitbucketClient _client = client;
    private readonly IServerSettings _serverSettings = serverSettings;
    private readonly ILogger<BitbucketCacheService> _logger = logger;

    // Core cache structures
    private List<Project> _projects = [];
    private readonly ConcurrentDictionary<string, Project> _projectLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<Repository>> _projectRepositories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Repository> _repositoryLookup = new(StringComparer.OrdinalIgnoreCase);

    // Tier 1: User context cache
    private User? _currentUser;
    private List<Repository> _recentRepositories = [];

    // Tier 1: Default branches per repository (projectKey/repoSlug -> branch)
    private readonly ConcurrentDictionary<string, Branch> _defaultBranches = new(StringComparer.OrdinalIgnoreCase);

    // Tier 1: Application properties
    private IDictionary<string, object?>? _applicationProperties;

    // Tier 1: Server capabilities
    private bool _isSearchAvailable;

    // I/O parallelism derived from host hardware.
    // API calls are I/O-bound, so we use ProcessorCount as the baseline (clamped to a sensible range).
    // Branch warmup uses a 2× multiplier because each call is a lightweight single-resource GET.
    private static readonly int IoParallelism = Math.Clamp(Environment.ProcessorCount, 2, 16);
    private static readonly int BranchWarmupParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Bitbucket cache (CPUs: {ProcessorCount}, I/O parallelism: {IoParallelism}, branch warmup: {BranchParallelism})...",
            Environment.ProcessorCount, IoParallelism, BranchWarmupParallelism);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Phase 1: Lightweight discovery (parallel) — no heavy enumeration
            await Task.WhenAll(
                InitializeApplicationPropertiesAsync(cancellationToken),
                InitializeCurrentUserAsync(cancellationToken),
                InitializeSearchAvailabilityAsync(cancellationToken)).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            // Phase 2: Recent repos (needs CurrentUser from Phase 1)
            await InitializeRecentRepositoriesAsync(cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            // Phase 3: Projects & repos — scoped to configured or recently-active projects
            var targetProjectKeys = ResolveTargetProjectKeys();
            await InitializeProjectsAndRepositoriesAsync(targetProjectKeys, cancellationToken).ConfigureAwait(false);

            LogCacheSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Bitbucket cache.");
        }
    }

    /// <summary>
    /// Determines which project keys to cache at startup.
    /// Priority: BITBUCKET_PROJECTS env var > project keys from recent repositories > empty (full fetch).
    /// </summary>
    private IReadOnlyList<string> ResolveTargetProjectKeys()
    {
        // Explicit configuration takes priority
        if (_serverSettings.Projects.Count > 0)
        {
            _logger.LogInformation("Using {Count} explicitly configured project(s): {Projects}.",
                _serverSettings.Projects.Count, string.Join(", ", _serverSettings.Projects));
            return _serverSettings.Projects;
        }

        // Derive from recent repositories when available
        if (_recentRepositories.Count > 0)
        {
            var projectKeys = _recentRepositories
                .Where(r => r.Project?.Key is not null)
                .Select(r => r.Project!.Key!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (projectKeys.Count > 0)
            {
                _logger.LogInformation("Derived {Count} project(s) from recent repositories: {Projects}.",
                    projectKeys.Count, string.Join(", ", projectKeys));
                return projectKeys;
            }
        }

        _logger.LogInformation("No project scope configured and no recent repositories found. Falling back to full project enumeration.");
        return [];
    }

    private async Task InitializeApplicationPropertiesAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _applicationProperties = await _client.GetApplicationPropertiesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Cached application properties (version: {Version}).",
                _applicationProperties.TryGetValue("version", out var version) ? version : "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache application properties.");
        }
    }

    private async Task InitializeCurrentUserAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use GetWhoAmIAsync to get the authenticated user's username
            var username = await _client.GetWhoAmIAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(username))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _currentUser = await _client.GetUserAsync(username, cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Authenticated as {DisplayName} ({UserSlug}).",
                    _currentUser.DisplayName, _currentUser.Slug);
            }
            else
            {
                _logger.LogWarning("GetWhoAmIAsync returned empty username. User-specific features may be limited.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache current user. User-specific features may be limited.");
        }
    }

    private async Task InitializeRecentRepositoriesAsync(CancellationToken cancellationToken)
    {
        if (_currentUser is null)
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recentRepos = await _client.GetRecentReposAsync(limit: 25, cancellationToken: cancellationToken).ConfigureAwait(false);
            _recentRepositories = recentRepos.ToList();
            _logger.LogInformation("Cached {Count} recent repositories for current user.", _recentRepositories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache recent repositories.");
        }
    }

    private async Task InitializeProjectsAndRepositoriesAsync(IReadOnlyList<string> targetProjectKeys, CancellationToken cancellationToken)
    {
        if (targetProjectKeys.Count > 0)
        {
            await InitializeScopedProjectsAsync(targetProjectKeys, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await InitializeAllProjectsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Fetch repositories for all cached projects
        await Parallel.ForEachAsync(_projects, new ParallelOptions { MaxDegreeOfParallelism = IoParallelism, CancellationToken = cancellationToken }, async (project, ct) =>
        {
            try
            {
                var repos = new List<Repository>();
                await foreach (var repo in _client.GetProjectRepositoriesStreamAsync(project.Key!, cancellationToken: ct))
                {
                    repos.Add(repo);
                }
                StoreRepositories(project.Key!, repos);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache repositories for project {ProjectKey}.", project.Key);
            }
        });

        var totalRepos = _projectRepositories.Values.Sum(r => r.Count);
        _logger.LogInformation("Cached {RepositoryCount} repositories across {ProjectCount} projects.", totalRepos, _projects.Count);
    }

    /// <summary>
    /// Fetches only the specified projects by key, tolerating missing projects.
    /// </summary>
    private async Task InitializeScopedProjectsAsync(IReadOnlyList<string> projectKeys, CancellationToken cancellationToken)
    {
        var projectsList = new List<Project>();

        await Parallel.ForEachAsync(projectKeys, new ParallelOptions { MaxDegreeOfParallelism = IoParallelism, CancellationToken = cancellationToken }, async (key, ct) =>
        {
            try
            {
                var project = await _client.GetProjectAsync(key, ct).ConfigureAwait(false);
                lock (projectsList)
                {
                    projectsList.Add(project);
                }
                _projectLookup[project.Key!] = project;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch project {ProjectKey}. It may not exist or you lack permission.", key);
            }
        });

        _projects = projectsList;
        _logger.LogInformation("Cached {Count} scoped projects.", _projects.Count);
    }

    /// <summary>
    /// Full enumeration of all projects (fallback when no scope is configured).
    /// </summary>
    private async Task InitializeAllProjectsAsync(CancellationToken cancellationToken)
    {
        var projectsList = new List<Project>();
        await foreach (var project in _client.Projects().StreamAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            projectsList.Add(project);
            _projectLookup[project.Key!] = project;
        }
        _projects = projectsList;
        _logger.LogInformation("Cached {Count} projects (full enumeration).", _projects.Count);
    }

    private async Task InitializeDefaultBranchesAsync(CancellationToken cancellationToken)
    {
        var allRepositories = _projectRepositories
            .SelectMany(kvp => kvp.Value.Select(r => (ProjectKey: kvp.Key, Repository: r)))
            .ToList();

        _logger.LogInformation("Background warmup: fetching default branches for {Count} repositories...", allRepositories.Count);

        await Parallel.ForEachAsync(allRepositories, new ParallelOptions { MaxDegreeOfParallelism = BranchWarmupParallelism, CancellationToken = cancellationToken }, async (item, ct) =>
        {
            try
            {
                var defaultBranch = await _client.GetDefaultBranchAsync(item.ProjectKey, item.Repository.Slug!, ct);
                if (defaultBranch is not null)
                {
                    var key = BuildRepositoryKey(item.ProjectKey, item.Repository.Slug!);
                    _defaultBranches[key] = defaultBranch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get default branch for {ProjectKey}/{RepoSlug}",
                    item.ProjectKey, item.Repository.Slug);
            }
        });

        _logger.LogInformation("Background warmup complete: cached {Count} default branches.", _defaultBranches.Count);
    }

    /// <summary>
    /// Pre-populates the default branch cache for all known repositories.
    /// Runs in the background and does not block server readiness.
    /// </summary>
    public Task WarmupDefaultBranchesAsync(CancellationToken cancellationToken = default) =>
        InitializeDefaultBranchesAsync(cancellationToken);

    private async Task InitializeSearchAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            _isSearchAvailable = await _client.IsSearchAvailableAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Server-side code search: {Status}.",
                _isSearchAvailable ? "available" : "not available");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to detect search availability. Defaulting to unavailable.");
            _isSearchAvailable = false;
        }
    }

    private void LogCacheSummary()
    {
        _logger.LogInformation(
            "Cache initialization complete. Projects: {Projects}, Repositories: {Repos}, Recent Repos: {Recent}, Search: {Search}. Default branches will load in background.",
            _projects.Count,
            _projectRepositories.Values.Sum(r => r.Count),
            _recentRepositories.Count,
            _isSearchAvailable ? "available" : "unavailable");
    }

    #region Public Accessors - Projects and Repositories

    public IEnumerable<Project> GetProjects() => _projects;

    public IEnumerable<Repository> GetRepositories(string projectKey)
    {
        if (_projectRepositories.TryGetValue(projectKey, out var repos))
        {
            return repos;
        }
        return [];
    }

    public IEnumerable<Repository> GetAllRepositories() =>
        _projectRepositories.Values.SelectMany(r => r);

    public Project? FindProject(string projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            return null;
        }

        _projectLookup.TryGetValue(projectKey, out var project);
        return project;
    }

    public Repository? FindRepository(string projectKey, string repositorySlug)
    {
        if (string.IsNullOrWhiteSpace(projectKey) || string.IsNullOrWhiteSpace(repositorySlug))
        {
            return null;
        }

        var key = BuildRepositoryKey(projectKey, repositorySlug);
        _repositoryLookup.TryGetValue(key, out var repo);
        return repo;
    }

    public void StoreRepositories(string projectKey, IEnumerable<Repository> repositories)
    {
        var repoList = repositories?.ToList() ?? [];
        _projectRepositories[projectKey] = repoList;

        foreach (var repo in repoList.Where(r => !string.IsNullOrEmpty(r.Slug)))
        {
            var key = BuildRepositoryKey(projectKey, repo.Slug!);
            _repositoryLookup[key] = repo;
        }
    }

    #endregion

    #region Public Accessors - User Context (Tier 1)

    public User? GetCurrentUser() => _currentUser;

    public string? GetCurrentUserSlug() => _currentUser?.Slug;

    public string? GetCurrentUserDisplayName() => _currentUser?.DisplayName;

    public IEnumerable<Repository> GetRecentRepositories() => _recentRepositories;

    public bool IsCurrentUser(string? userSlugOrName)
    {
        if (string.IsNullOrWhiteSpace(userSlugOrName) || _currentUser is null)
        {
            return false;
        }

        return string.Equals(_currentUser.Slug, userSlugOrName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(_currentUser.Name, userSlugOrName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(_currentUser.DisplayName, userSlugOrName, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Public Accessors - Default Branches (Tier 1)

    /// <summary>
    /// Gets the default branch for a repository. If not yet cached, fetches it from the API.
    /// </summary>
    public async ValueTask<Branch?> GetDefaultBranchAsync(string projectKey, string repositorySlug, CancellationToken cancellationToken = default)
    {
        var key = BuildRepositoryKey(projectKey, repositorySlug);
        if (_defaultBranches.TryGetValue(key, out var branch))
        {
            return branch;
        }

        try
        {
            var fetched = await _client.GetDefaultBranchAsync(projectKey, repositorySlug, cancellationToken).ConfigureAwait(false);
            if (fetched is not null)
            {
                _defaultBranches[key] = fetched;
            }
            return fetched;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch default branch for {ProjectKey}/{RepoSlug}", projectKey, repositorySlug);
            return null;
        }
    }

    public async ValueTask<string?> GetDefaultBranchNameAsync(string projectKey, string repositorySlug, CancellationToken cancellationToken = default)
    {
        var branch = await GetDefaultBranchAsync(projectKey, repositorySlug, cancellationToken).ConfigureAwait(false);
        return branch?.DisplayId;
    }

    public async ValueTask<string> GetDefaultBranchRefAsync(string projectKey, string repositorySlug, string fallback = "refs/heads/master", CancellationToken cancellationToken = default)
    {
        var branch = await GetDefaultBranchAsync(projectKey, repositorySlug, cancellationToken).ConfigureAwait(false);
        return branch?.Id ?? fallback;
    }

    public void StoreDefaultBranch(string projectKey, string repositorySlug, Branch branch)
    {
        var key = BuildRepositoryKey(projectKey, repositorySlug);
        _defaultBranches[key] = branch;
    }

    #endregion

    #region Public Accessors - Application Properties (Tier 1)

    public IDictionary<string, object?>? GetApplicationProperties() => _applicationProperties;

    public string? GetServerVersion()
    {
        if (_applicationProperties?.TryGetValue("version", out var version) == true)
        {
            return version?.ToString();
        }
        return null;
    }

    public string? GetServerDisplayName()
    {
        if (_applicationProperties?.TryGetValue("displayName", out var name) == true)
        {
            return name?.ToString();
        }
        return null;
    }

    public string? GetBuildNumber()
    {
        if (_applicationProperties?.TryGetValue("buildNumber", out var buildNumber) == true)
        {
            return buildNumber?.ToString();
        }
        return null;
    }

    #endregion

    #region Public Accessors - Server Capabilities

    public bool IsSearchAvailable() => _isSearchAvailable;

    #endregion

    #region Helpers

    private static string BuildRepositoryKey(string projectKey, string repositorySlug) =>
        $"{projectKey}/{repositorySlug}";

    #endregion
}