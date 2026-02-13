## Architecture

This document describes the architecture of the Stash MCP Server, a Model Context Protocol server implementation for Atlassian Stash/Bitbucket Server.

### Folder Structure

```
src/StashMcpServer/
├── Program.cs                         # Entry point — stdio transport
├── StashMcpServer.csproj
│
├── Configuration/
│   ├── CommandLineParser.cs           # CLI argument and env var parsing
│   ├── ServerSettings.cs              # Runtime server settings (IServerSettings)
│   └── ResilienceSettings.cs          # Retry, circuit breaker, cache settings
│
├── Extensions/
│   └── ServiceCollectionExtensions.cs # DI registration helpers
│
├── Logging/
│   ├── McpLogDispatcher.cs            # Background service dispatching logs to MCP
│   ├── McpLogQueue.cs                 # Thread-safe log message queue
│   └── McpSerilogSink.cs             # Serilog sink bridging to MCP log queue
│
├── Services/
│   ├── IBitbucketCacheService.cs      # Interface — cache abstraction
│   ├── BitbucketCacheService.cs       # IMemoryCache + ConcurrentDict caching
│   ├── IResilientApiService.cs        # Interface — resilient API calls
│   ├── ResilientApiService.cs         # Polly-based retry + circuit breaker
│   ├── IServerSettings.cs             # Interface — server configuration
│   ├── CacheKeys.cs                   # Cache key generator
│   ├── ServerInstructions.cs          # MCP server instructions text
│   └── StartupService.cs             # Hosted service for startup validation
│
├── Formatting/
│   ├── IDiffFormatter.cs              # Interface — diff formatting
│   ├── DiffFormatter.cs               # Formats diff output for readability
│   ├── MinimalOutputFormatter.cs      # Compact output for list operations
│   └── ResponseTruncation.cs          # 50KB response size limiter
│
└── Tools/
    ├── ToolBase.cs                    # Abstract base — shared dependencies & helpers
    ├── ToolHelpers.cs                 # Static utilities (NormalizeRef, etc.)
    ├── Projects/ProjectTools.cs       # list_projects
    ├── Repositories/RepositoryTools.cs # list_repositories, get_repository_overview, etc.
    ├── PullRequests/PullRequestTools.cs # 8 PR tools + 4 comment + 4 task tools
    ├── Search/SearchTools.cs          # search_code, search_commits, search_pull_requests, search_users
    ├── Git/GitTools.cs                # list_branches, list_tags, list_files
    ├── History/HistoryTools.cs        # get_commit, get_commit_changes, get_commit_context
    ├── Builds/BuildTools.cs           # get_build_status, get_pull_request_build_status, list_builds
    ├── Dashboard/DashboardTools.cs    # get_my_pull_requests, get_inbox_pull_requests
    └── Integrations/IntegrationTools.cs # get_pr_jira_issues

src/McpServerFactory/
├── McpServerFactory.csproj            # Reusable MCP integration testing library
├── McpServerFactory.cs                # In-memory host + client wiring factory
├── McpServerFactoryOptions.cs         # Test server configuration options
├── McpTestClient.cs                   # Optional convenience wrapper for tests
└── README.md                          # Usage and API guide

tests/StashMcpServer.IntegrationTests/
├── StashMcpServer.IntegrationTests.csproj
├── StashMcpTestFactory.cs             # stash-mcp specific factory subclass
├── ToolDiscoveryTests.cs              # Tool registration/discovery coverage
├── ProjectToolsIntegrationTests.cs    # Project tool integration coverage
├── PullRequestToolsIntegrationTests.cs # Pull request write-guard coverage
└── EdgeCaseTests.cs                   # Error handling and lifecycle coverage
```

### Layers

The application follows a layered architecture with clear separation of concerns:

```
        ┌────────────────────────────────────────┐
        │           MCP Transport Layer           │
        │              stdio (JSON-RPC)            │
        └─────────────────┬──────────────────────┘
                          │
        ┌─────────────────▼──────────────────────┐
        │           Tool Layer (9 classes)         │
        │   Each class: [McpServerToolType]        │
        │   Inherits ToolBase for shared helpers   │
        │   Discovered via assembly scanning       │
        └─────────────────┬──────────────────────┘
                          │
        ┌─────────────────▼──────────────────────┐
        │         Formatting Layer                 │
        │   DiffFormatter, ResponseTruncation,     │
        │   MinimalOutputFormatter                 │
        └─────────────────┬──────────────────────┘
                          │
        ┌─────────────────▼──────────────────────┐
        │       ResilientApiService                │
        │   Polly: retry + circuit breaker         │
        │   Graceful degradation with stale cache  │
        └─────────────────┬──────────────────────┘
                          │
        ┌─────────────────▼──────────────────────┐
        │       BitbucketCacheService              │
        │   IMemoryCache with configurable TTL     │
        │   ConcurrentDict for static data         │
        └─────────────────┬──────────────────────┘
                          │
        ┌─────────────────▼──────────────────────┐
        │     BitbucketServer.Net Client           │
        │     REST API calls to Bitbucket Server   │
        └──────────────────────────────────────────┘
```

### Key Design Decisions

**Domain-Separated Tools**: Each functional area (projects, pull requests, search, etc.) has its own tool class. Tool classes are discovered at startup via `WithToolsFromAssembly()` — no manual registration needed.

**ToolBase Pattern**: All tool classes inherit `ToolBase`, which provides:
- Common dependencies via constructor (logger, cache, API service, settings)
- Shared helpers: `NormalizeProjectKey`, `NormalizeRepositorySlug`, `CheckReadOnlyMode`, `LogToolInvocation`
- Consistent error handling and logging

**Interface-Driven Services**: Core services (`IBitbucketCacheService`, `IResilientApiService`, `IServerSettings`, `IDiffFormatter`) and the Bitbucket API client (`IBitbucketClient`) are registered via interfaces, enabling unit testing with mocks and cleaner dependency boundaries.

**Fluent Query Builders**: Tool implementations prefer query builders (`PullRequests(...)`, `Commits(...)`, `Branches(...)`, `Projects(...)`) for parameter-heavy reads. This reduces positional argument complexity and keeps API intent explicit while preserving the same underlying REST calls.

**Stdio Transport**: The server uses stdio (stdin/stdout) for MCP communication. This is the standard transport for MCP servers running as Docker containers or local processes, used by VS Code, Claude Desktop, and other MCP clients.

**Scoped Startup Caching**: Instead of enumerating all projects and repositories at startup, the cache is scoped to the user's active projects. The resolution order is:
1. `BITBUCKET_PROJECTS` env var — explicit comma-separated project keys
2. Project keys derived from the authenticated user's recent repositories
3. Full enumeration (fallback when no scope can be determined)

Tools that reference non-cached projects fall through gracefully: `NormalizeProjectKey` returns the raw key, and `list_repositories` fetches from the API on cache miss.

**Resilience**: All Bitbucket API calls go through `ResilientApiService`, which wraps each call with:
- Retry with exponential backoff (configurable, default 3 attempts)
- Circuit breaker (configurable timeout)
- Graceful degradation to stale cached data when the API is unavailable

**Response Safety**: Output is truncated at 50KB to prevent context window overflow in AI clients.

### Integration Testing Architecture

stash-mcp now includes a dedicated integration testing stack built around
`McpServerFactory`:

- `McpServerFactory` creates an in-memory client/server MCP connection using
    `System.IO.Pipelines.Pipe` and stream transports.
- `StashMcpTestFactory` (in integration tests) registers all stash tools via
    `WithToolsFromAssembly(...)` and replaces infrastructure dependencies with
    test doubles.
- Integration tests call tools through real MCP protocol flow (`ListToolsAsync`,
    `CallToolAsync`) instead of directly invoking tool methods.

This gives end-to-end confidence for tool registration, parameter binding,
result serialization, read-only guards, and lifecycle/disposal behavior while
remaining fully local and deterministic.

### Dependencies

| Package | Purpose |
|---------|---------|
| ModelContextProtocol | MCP server framework (stdio transport) |
| BitbucketServer.Net | Bitbucket Server REST API client |
| Serilog.Extensions.Logging | Structured logging |
| Microsoft.Extensions.Resilience | Polly integration for retry/circuit breaker |
| Meziantou.Analyzer | Code quality analysis |
