# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
