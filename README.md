# Stash MCP Server

A Model Context Protocol (MCP) server implementation for Atlassian Stash/Bitbucket Server, enabling AI assistants to interact with your repositories, pull requests, and code review workflows.

## Features

**40 tools** covering comprehensive Bitbucket Server workflows:

| Category | Tools | Highlights |
|----------|-------|-----------|
| **Projects** | 1 | Discover projects |
| **Dashboard** | 5 | User-centric PR views + server info |
| **Repositories** | 3 | Repos, overview, file content |
| **Git** | 3 | Branches, tags, file listing |
| **Search** | 4 | Code, commits, PRs, users |
| **History** | 5 | List/inspect commits, compare refs, full commit context |
| **Pull Requests** | 7 | List/get/context/diff + create/update + approve |
| **Comments** | 4 | Read, write, reply with code context |
| **Tasks** | 4 | Full CRUD for review tasks |
| **Builds** | 3 | CI/CD status per commit/PR/repo |
| **Integrations** | 1 | Jira issue links |

### ⭐ Workflow-Optimized Tools

These tools reduce multiple API calls to a single invocation:

- `get_pull_request_context` - Complete PR with comments, tasks, diff, activity
- `get_repository_overview` - Branches, tags, and open PRs in one call
- `get_commit_context` - Commit details with changes and diff

### Resilience Features

- **Circuit Breaker**: Prevents cascading failures when Bitbucket is unavailable
- **Retry with Backoff**: Automatic retry for transient errors (429, 502, 503, 504)
- **Graceful Degradation**: Returns cached data when API fails
- **Response Truncation**: 50KB limit prevents context overflow
- **Cache Invalidation**: Write operations automatically refresh related caches

## Getting Started

### Prerequisites

- .NET 10.0 SDK (or use pre-built executable)
- Bitbucket Server instance (self-hosted)
- Personal Access Token with repository read/write permissions

### Configuration

The server can be configured via command-line arguments or environment variables (see [Configuration Reference](#configuration-reference) for full details):

| Parameter | Environment Variable | Description |
|-----------|---------------------|-------------|
| `--stash-url` | `BITBUCKET_URL` | Bitbucket Server base URL (required) |
| `--pat` | `BITBUCKET_TOKEN` | Personal Access Token (required) |
| `--log-level` | - | Logging level: Verbose, Debug, Information (default), Warning, Error |
| `--transport` | `MCP_TRANSPORT` | Transport mode: `stdio` (default) or `http` |

### Running the Server

**Using command-line arguments:**

```bash
StashMcpServer.exe --stash-url https://your-stash-server.com/ --pat your_personal_access_token
```

**Using environment variables:**

```bash
# Set environment variables
export BITBUCKET_URL=https://your-stash-server.com/
export BITBUCKET_TOKEN=your_personal_access_token

# Run without arguments
StashMcpServer.exe
```

## Docker

The server supports HTTP transport for container deployments. When running in Docker, the server exposes an HTTP endpoint at `/` instead of using stdio.

### Quick Start

```bash
docker run -d \
  -p 8080:8080 \
  -e BITBUCKET_URL=https://your-stash-server.com/ \
  -e BITBUCKET_TOKEN=your_personal_access_token \
  ghcr.io/diomonogatari/stash-mcp:latest
```

### Building Locally

```bash
docker build -t stash-mcp .
docker run -d -p 8080:8080 \
  -e BITBUCKET_URL=https://your-stash-server.com/ \
  -e BITBUCKET_TOKEN=your_personal_access_token \
  stash-mcp
```

### MCP Configuration (HTTP Transport)

When using the Docker container, configure your MCP client to connect via HTTP:

```json
{
  "servers": {
    "stash-bitbucket": {
      "type": "http",
      "url": "http://localhost:8080/"
    }
  }
}
```

### Environment Variables for Docker

All [configuration settings](#configuration-reference) work as environment variables in Docker:

```bash
docker run -d \
  -p 8080:8080 \
  -e BITBUCKET_URL=https://your-stash-server.com/ \
  -e BITBUCKET_TOKEN=your_personal_access_token \
  -e BITBUCKET_RETRY_COUNT=5 \
  -e BITBUCKET_CIRCUIT_TIMEOUT=60 \
  -e BITBUCKET_CACHE_TTL_SECONDS=120 \
  -e BITBUCKET_READ_ONLY_MODE=false \
  ghcr.io/diomonogatari/stash-mcp:latest
```

## VS Code Integration

### Pre-built Executable

You can find `StashMcpServer.exe` in the `./artifacts` folder, or build it yourself using the task `build release StashMcpServer`.

### MCP Configuration

Add the following to your VS Code `mcp.json` (open via Command Palette: `MCP: Open user configuration`).

#### Option 1: Using Command-Line Arguments (stdio)

Best for simple configurations where you only need the required parameters:

```json
{
  "servers": {
    "stash-bitbucket": {
      "type": "stdio",
      "command": "c:\\path\\to\\StashMcpServer.exe",
      "args": [
        "--stash-url", "https://your-stash-server.com/",
        "--pat", "your_personal_access_token",
        "--log-level", "Information"
      ]
    }
  }
}
```

#### Option 2: Using Environment Variables

Best when you want to configure resilience settings or keep credentials separate:

```json
{
  "servers": {
    "stash-bitbucket": {
      "type": "stdio",
      "command": "c:\\path\\to\\StashMcpServer.exe",
      "env": {
        "BITBUCKET_URL": "https://your-stash-server.com/",
        "BITBUCKET_TOKEN": "your_personal_access_token"
      }
    }
  }
}
```

#### Option 3: Mixed Configuration (Recommended)

Combine args for basic settings and env for advanced tuning:

```json
{
  "servers": {
    "stash-bitbucket": {
      "type": "stdio",
      "command": "c:\\path\\to\\StashMcpServer.exe",
      "args": [
        "--stash-url", "https://your-stash-server.com/",
        "--pat", "your_personal_access_token",
        "--log-level", "Debug"
      ],
      "env": {
        "BITBUCKET_RETRY_COUNT": "5",
        "BITBUCKET_CIRCUIT_TIMEOUT": "60",
        "BITBUCKET_CACHE_TTL_SECONDS": "120",
        "BITBUCKET_READ_ONLY_MODE": "false"
      }
    }
  }
}
```

#### Configuration Reference

| Setting | Arg | Env Variable | Default | Description |
|---------|-----|--------------|---------|-------------|
| Server URL | `--stash-url` | `BITBUCKET_URL` | - | Bitbucket Server base URL (required) |
| Access Token | `--pat` | `BITBUCKET_TOKEN` | - | Personal Access Token (required) |
| Log Level | `--log-level` | - | Information | Verbose, Debug, Information, Warning, Error |
| Transport | `--transport` | `MCP_TRANSPORT` | stdio | Transport mode: `stdio` or `http` |
| Retry Count | - | `BITBUCKET_RETRY_COUNT` | 3 | Max retry attempts (0-10) |
| Circuit Timeout | - | `BITBUCKET_CIRCUIT_TIMEOUT` | 30 | Circuit breaker duration in seconds (5-300) |
| Cache TTL | - | `BITBUCKET_CACHE_TTL_SECONDS` | 60 | Cache time-to-live in seconds (10-600) |
| Read-Only Mode | - | `BITBUCKET_READ_ONLY_MODE` | false | Disable write operations (`true` or `1`) |

> **Note**: Command-line arguments take precedence over environment variables for settings that support both.

## Tool Reference

For detailed documentation of all 40 tools, see [docs/TOOLSET.md](docs/TOOLSET.md).

### Common Workflows

#### Code Review
```
1. get_pull_request_context (with includeComments=true, includeDiff=true)
2. Review the diff and existing comments
3. add_pull_request_comment (for feedback)
4. create_pull_request_task (for required changes)
5. approve_pull_request (when satisfied)
```

#### Bug Investigation
```
1. search_commits (messageContains="JIRA-123")
2. get_commit_context (includeDiff=true)
3. search_code (to find current implementation)
```

#### Repository Exploration
```
1. get_repository_overview (quick overview)
2. list_files (browse structure)
3. get_file_content (read specific files)
```

### Output Optimization

For list operations, use `minimalOutput=true` to reduce response size:
- `list_repositories` - Returns repository slugs only
- `list_branches` - Returns branch names only
- `list_pull_requests` - Returns compact PR summary

## Local Development

### Setup

1. Clone the repository **with submodules**:
   ```bash
   git clone --recurse-submodules https://github.com/diomonogatari/stash-mcp.git
   ```
   If you already cloned without submodules, initialize them:
   ```bash
   git submodule update --init --recursive
   ```
2. Ensure you have the .NET 10.0 SDK installed
3. Configure your Bitbucket credentials via environment variables or command-line arguments

### Building

```bash
# Build the solution
dotnet build stash-mcp.slnx

# Run tests
dotnet test stash-mcp.slnx

# Run the server (stdio mode)
dotnet run --project src/StashMcpServer/StashMcpServer.csproj -- --stash-url https://your-server.com/ --pat your_pat

# Run the server (HTTP mode for local Docker testing)
dotnet run --project src/StashMcpServer/StashMcpServer.csproj -- --stash-url https://your-server.com/ --pat your_pat --transport http
```

Note the double dash (`--`) that separates dotnet run arguments from application arguments.

### Build Release

```bash
dotnet publish src/StashMcpServer/StashMcpServer.csproj --configuration Release --output ./artifacts --self-contained true /p:PublishSingleFile=true
```

Or use the VS Code task: `build release StashMcpServer`

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                      MCP Server Layer                    │
│  ┌──────────────────────────────────────────────────┐    │
│  │ Domain Tool Classes (9 classes, 40 tools)        │    │
│  │  ProjectTools  RepositoryTools  PullRequestTools  │    │
│  │  SearchTools   GitTools   HistoryTools            │    │
│  │  BuildTools    DashboardTools  IntegrationTools    │    │
│  └──────────┬───────────────────────────────────────┘    │
│             │ inherits ToolBase (shared helpers)          │
│  ┌──────────▼───────────────────────────────────────┐    │
│  │ Formatting  │  DiffFormatter  ResponseTruncation  │    │
│  │             │  MinimalOutputFormatter (50KB limit) │    │
│  └──────────┬───────────────────────────────────────┘    │
│             │                                            │
│  ┌──────────▼───────────────────────────────────────┐    │
│  │           ResilientApiService                     │    │
│  │  • Circuit Breaker (Polly)                        │    │
│  │  • Retry with Exponential Backoff                 │    │
│  │  • Graceful Degradation (stale cache)             │    │
│  │  • Cache Invalidation on Writes                   │    │
│  └──────────┬───────────────────────────────────────┘    │
│             │                                            │
│  ┌──────────▼──────┐ ┌──────────────────────────────┐    │
│  │ Cache Layer      │ │ IMemoryCache (TTL=60s)       │    │
│  │ (Static)         │ │ ConcurrentDict (projects)    │    │
│  └──────────┬──────┘ └──────────────────────────────┘    │
│             │                                            │
│  Transport: stdio (local) | HTTP /mcp (Docker)           │
└─────────────┼────────────────────────────────────────────┘
              │
              ▼
       Bitbucket Server API (via Bitbucket.Net submodule)
```

For detailed architecture documentation, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Security Note

⚠️ **Never commit your Personal Access Token to source control.**

Consider:
- Using environment variables
- Using secure credential storage
- Restricting PAT permissions to minimum required scopes

## Documentation

- [Architecture](docs/ARCHITECTURE.md) - System design and folder structure
- [Tool Reference](docs/TOOLSET.md) - Detailed documentation for all 40 tools
- [Changelog](CHANGELOG.md) - Version history and release notes

## License

See [LICENSE](LICENSE) for details.
