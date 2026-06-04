# Bitbucket Server (Stash) MCP Server

An MCP server for **Atlassian Bitbucket Server (Stash)** — the self-hosted/on-prem
product (not Bitbucket Cloud). It gives AI assistants access to your repositories,
pull requests, code review, commits, builds, and search through **41 purpose-built
tools**, including workflow-optimized tools that collapse several API calls into a
single invocation (e.g. full PR context, repository overview, commit context).

## Configuration

| Setting | Variable | Required | Description |
|---------|----------|----------|-------------|
| Server URL | `BITBUCKET_URL` | yes | Base URL of your Bitbucket Server instance, e.g. `https://stash.example.com/` |
| Access Token | `BITBUCKET_TOKEN` | yes | Personal Access Token with repository **read** (and **write** for PR actions) |

Optional tuning (`BITBUCKET_READ_ONLY_MODE`, `BITBUCKET_PROJECTS`, retry/cache settings)
is documented in the project README.

## Tool categories

- **Projects & Repositories** — list projects/repos, repository overview, file content
- **Git** — branches, tags, file listing
- **Pull Requests** — list/get/context/diff, create/update/merge, approve
- **Comments & Tasks** — read/write comments and review tasks with code context
- **History** — list/inspect commits, compare refs, full commit context
- **Search** — code, commits, pull requests, users
- **Builds** — CI/CD status per commit, PR, and repository
- **Dashboard** — personal PR views, inbox, recent repositories, server info
- **Integrations** — linked Jira issues

## Links

- Source & full documentation: [github.com/diomonogatari/stash-mcp](https://github.com/diomonogatari/stash-mcp)
- Tool reference: [docs/TOOLSET.md](https://github.com/diomonogatari/stash-mcp/blob/main/docs/TOOLSET.md)
- License: MIT
