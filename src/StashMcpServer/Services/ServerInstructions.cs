namespace StashMcpServer.Services;

/// <summary>
/// Generates server instructions for LLM consumers of the MCP server.
/// These instructions guide optimal tool selection for common workflows.
/// </summary>
public static class ServerInstructions
{
    /// <summary>
    /// Generates comprehensive server instructions for LLM tool selection guidance.
    /// </summary>
    /// <returns>Server instructions as a formatted string.</returns>
    public static string Generate()
    {
        return
"""
# Bitbucket (Stash) MCP Server Instructions

This server provides tools for interacting with Bitbucket Server (Stash) repositories, pull requests, commits, and code. Use these instructions to select the most appropriate tools for each task.

## Tool Selection Guidelines

### Starting Point: Discovery Tools

When the user hasn't specified a project or repository:
- Use `list_projects` to discover available projects
- Use `list_repositories` with `projectKey` to list repositories in a project
- Use `get_repository_overview` for comprehensive repository information (recommended for exploration)

### Dashboard & Personal Workload

For user-centric views of their work:
- `get_my_pull_requests` - Find PRs you authored, are reviewing, or participating in
- `get_inbox_pull_requests` - Find PRs in your review inbox needing attention

### Pull Request Analysis

**For comprehensive PR review (RECOMMENDED):**
Use `get_pull_request_context` - provides metadata, reviewers, comments, tasks, and optional diff in a single call.

**For specific PR data:**
- `get_pull_request` - Quick metadata only (title, state, author, branches)
- `get_pull_request_diff` - Just the diff text
- `get_pull_request_comments` - All comments with code context
- `get_pull_request_unresolved_comments` - Only open discussions needing attention

### Commit Analysis

**For comprehensive commit review (RECOMMENDED):**
Use `get_commit_context` with `includeDiff=true` - provides commit details, changed files, and diff in a single call.

**For specific commit data:**
- `get_commit` - Quick commit metadata only
- `get_commit_changes` - List of changed files without diff content

### Search Operations

**Searching for code:**
- `search_code` - Find code patterns in repository files (grep-style search)
  - Use `pathPattern` to narrow scope for better performance
  - Supports regex and case-sensitive options

**Searching for commits:**
- `search_commits` - Search by message, author, date range
  - Omit `repositorySlug` to search across all repositories in project
  - Use `since`/`until` for date filtering

**Searching for pull requests:**
- `search_pull_requests` - Search by title, description, author, state
  - Omit `repositorySlug` for cross-repository search

### Creating & Managing PRs

**Creating PRs:**
- `create_pull_request` - Always requires `title`, `sourceRef`, `targetRef`
- Target ref defaults to repository default branch if omitted

**Updating PRs:**
- `update_pull_request` - Update title, description, add/remove reviewers
- `approve_pull_request` - Approve a PR

### Comments & Tasks

**Reading comments:**
- Prefer `get_pull_request_context` with `includeComments=true` for full context
- Use `get_pull_request_unresolved_comments` to find open discussions

**Adding comments:**
- `add_pull_request_comment` - General or line-specific comments
- `reply_to_pull_request_comment` - Reply to existing comment thread

**Managing tasks:**
- Tasks are anchored to comments and track required actions before merge
- `get_pull_request_tasks` - List all tasks (open/resolved)
- `create_pull_request_task` - Create task on a comment (use `get_pull_request_comments` first to find comment IDs)
- Note: Task state changes (resolve/reopen) are done through Bitbucket UI, not API

### Build Status

- `get_build_status` - CI/CD status for any commit (by commit hash)
- `get_pull_request_build_status` - Convenience tool for PR head commit

### Output Optimization

For list operations, use `minimalOutput=true` to reduce response size:
- `list_repositories` - Returns repository slugs only
- `list_branches` - Returns branch names only
- `list_pull_requests` - Returns compact PR summary (ID, title, state, author)

## Common Workflows

### Code Review Workflow
1. `get_pull_request_context` with `includeComments=true` and `includeDiff=true`
2. Review the diff and existing comments
3. Use `add_pull_request_comment` for feedback (general or line-specific)
4. Use `create_pull_request_task` for required changes
5. Use `approve_pull_request` when satisfied

### Bug Investigation Workflow
1. `search_commits` with `messageContains` to find related commits
2. `get_commit_context` with `includeDiff=true` to understand changes
3. `search_code` to find current implementation

### Repository Exploration
1. `get_repository_overview` for quick overview
2. `list_files` to browse file structure
3. `get_file_content` to read specific files

### PR Creation Workflow
1. Ensure branch is pushed to remote
2. `create_pull_request` with title, source/target branches, description
3. Optionally add reviewers via `update_pull_request`

## Caching Behavior

- Static data (projects, repositories) is cached indefinitely
- Dynamic data (PRs, comments, builds) is cached for 60 seconds by default
- Write operations automatically invalidate related caches
- The server handles retry and circuit breaker logic automatically

## Error Handling

- The server returns helpful error messages for common issues
- For connection issues, check BITBUCKET_URL and BITBUCKET_TOKEN configuration
- For permission errors, verify the PAT has appropriate repository access
""";
    }
}