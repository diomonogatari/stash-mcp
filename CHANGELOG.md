# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-02-13

### Added

- New `tests/StashMcpServer.IntegrationTests` project for end-to-end MCP integration testing
- In-memory MCP test harness via `McpServerFactory` NuGet package (`0.1.0`)
- Integration coverage for:
  - MCP tool discovery and registration
  - `ProjectTools` behavior
  - Pull request tool behavior in read-only mode
  - Edge-case lifecycle scenarios for test host/client setup
- `StashMcpTestFactory` with mocked Bitbucket dependencies for deterministic integration tests

### Changed

- Added central package versioning entry for `McpServerFactory` in `Directory.Packages.props`
- Added integration test project to `stash-mcp.slnx`
- Updated architecture and tooling documentation to reflect current implementation:
  - `docs/ARCHITECTURE.md`
  - `docs/TOOLSET.md`
  - `src/StashMcpServer/Services/ServerInstructions.txt`
- Tightened model serialization test coverage for code search and diff payloads

### Fixed

- Null-safety in server-side search result handling (`SearchTools`) to prevent nullable dereference warnings
- Markdown link-fragment correctness in `docs/TOOLSET.md` (`list_branches` section anchor)

## [1.1.0] - 2026-02-10

### Added

- `BITBUCKET_PROJECTS` environment variable to scope startup caching to specific project keys (e.g. `PROJ,TEAM`)
- Lazy-loading of default branches: fetched on demand per repository instead of eagerly at startup
- Background warmup of default branches after the server reports ready
- Hardware-aware I/O parallelism derived from `Environment.ProcessorCount` (clamped to sensible ranges for API and branch warmup calls)
- `STOPSIGNAL SIGTERM` in Dockerfile for explicit signal handling
- `Console.Out` redirect to `TextWriter.Null` to prevent accidental stdout writes from corrupting the JSON-RPC stream
- `Log.CloseAndFlushAsync()` in shutdown path to ensure all buffered Serilog events are flushed

### Changed

- Startup caching now scoped to user's active projects instead of enumerating all projects and repositories. Priority: `BITBUCKET_PROJECTS` env var → project keys from recent repositories → full enumeration (fallback)
- Cache initialization no longer blocks server readiness on default branch fetching (~1800 API calls moved to background), reducing startup time from ~30s to ~5-10s
- Default branch accessors in `IBitbucketCacheService` are now async (`ValueTask`-based) with on-demand API fetch on cache miss
- MCP initialization timeout reduced from 30s to 15s
- Transport simplified to stdio-only, matching how all MCP Docker servers operate
- Docker runtime image from `aspnet:10.0` to `runtime:10.0` (smaller image, no ASP.NET Core overhead)
- Project SDK from `Microsoft.NET.Sdk.Web` to `Microsoft.NET.Sdk`
- Replaced `Serilog.AspNetCore` with `Serilog.Extensions.Logging` (lighter dependency)

### Fixed

- Docker containers remaining alive after VS Code stops the MCP server (stdin EOF now handled natively by MCP SDK in stdio mode)
- Server unresponsive to MCP `initialize` request when running as Docker container

### Removed

- HTTP transport mode — the server now uses stdio exclusively
- `--transport` CLI flag
- `ModelContextProtocol.AspNetCore` dependency

## [1.0.0] - 2026-02-10

### Added

- Solution-level infrastructure: `global.json`, `Directory.Build.props`, `Directory.Packages.props`
- Central Package Management for consistent dependency versions
- Meziantou.Analyzer for enhanced code quality checks
- NSubstitute testing library for mocking support
- `CHANGELOG.md` following Keep a Changelog format
- GitHub Actions CI/CD workflows for build and release

### Changed

- Restructured from monolithic `StashTools` God class into per-domain tool classes
- Extracted shared helpers into `ToolBase` abstract class and `ToolHelpers` static class
- Introduced `IBitbucketCacheService`, `IResilientApiService`, and `IServerSettings` interfaces
- Reorganized folder structure with `Configuration/`, `Formatting/`, and domain-specific `Tools/` folders
- Cleaned up `Program.cs` with service collection extensions and extracted command-line parsing
- Server version now derived from assembly version instead of hardcoded value
- Added Docker container support for running the MCP server

### Removed

- `.env` / `.env.example` configuration approach (replaced with CLI args and `env` in MCP config)
- Partial class `StashTools` monolith

## [0.9.0] - 2025-01-15

### Added

- Initial release with 40 MCP tools covering Bitbucket Server workflows
- Pull request management (CRUD, approve, context)
- Comment and task management
- Repository exploration and file content retrieval
- Code, commit, and pull request search
- Build status and CI/CD integration
- Dashboard with personal workload views
- Jira integration for linked issues
- Circuit breaker and retry resilience with Polly
- Response truncation (50KB limit)
- TTL-based caching with graceful degradation
- MCP logging bridge via Serilog
