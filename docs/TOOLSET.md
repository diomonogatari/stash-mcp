# Toolset

## Overview

| Functional Area | Tool | Description |
| --------------- | ---- | ----------- |
| Projects | [list_projects](#list_projects) | List all available projects to discover repositories |
| Dashboard | [get_my_pull_requests](#get_my_pull_requests) | Get PRs you authored, are reviewing, or participating in |
| Dashboard | [get_inbox_pull_requests](#get_inbox_pull_requests) | Get PRs in your review inbox needing attention |
| Dashboard | [get_recent_repositories](#get_recent_repositories) | Get repositories the current user has recently accessed |
| Dashboard | [get_server_info](#get_server_info) | Get Bitbucket Server info, version, and cache stats |
| Dashboard | [get_current_user](#get_current_user) | Get information about the currently authenticated user |
| Repositories | [list_repositories](#list_repositories) | List repositories within a specific project |
| Repositories | [get_repository_overview](#get_repository_overview) | ⭐ Get comprehensive repository info (branches, tags, PRs) |
| Repositories | [get_file_content](#get_file_content) | Get the raw content of a file in a repository |
| Git | [list_branches](#list_branches) | List all branches in a repository |
| Git | [list_tags](#list_tags) | List tags to identify release points |
| Git | [list_files](#list_files) | List files in a repository at a specific commit or branch |
| Search | [search_code](#search_code) | Search for code patterns in repository files |
| Search | [search_commits](#search_commits) | Search commit history by message, author, date range |
| Search | [search_pull_requests](#search_pull_requests) | Search PRs by title, description, author, or state |
| Search | [search_users](#search_users) | 🆕 Search for users by name, username, or email |
| History | [list_commits](#list_commits) | 🆕 List commits in a repository with optional filtering |
| History | [get_commit](#get_commit) | Get details of a specific commit |
| History | [get_commit_changes](#get_commit_changes) | View the files changed in a specific commit |
| History | [get_commit_context](#get_commit_context) | ⭐ Get complete commit details with changes and diff |
| History | [compare_refs](#compare_refs) | Compare the diff between two refs (branches, tags, or commits) |
| Pull Requests | [list_pull_requests](#list_pull_requests) | List pull requests for a repository with optional filtering |
| Pull Requests | [get_pull_request](#get_pull_request) | Get detailed metadata for a specific pull request |
| Pull Requests | [get_pull_request_context](#get_pull_request_context) | ⭐ Get complete PR context (metadata, comments, diff) |
| Pull Requests | [get_pull_request_diff](#get_pull_request_diff) | Get the diff text for a specific pull request |
| Pull Requests | [create_pull_request](#create_pull_request) | Create a new pull request from source to target branch |
| Pull Requests | [update_pull_request](#update_pull_request) | Update pull request metadata (title, description, reviewers) |
| Pull Requests | [approve_pull_request](#approve_pull_request) | Approve a pull request |
| Comments | [get_pull_request_comments](#get_pull_request_comments) | Get all comments for a pull request with code context |
| Comments | [get_pull_request_unresolved_comments](#get_pull_request_unresolved_comments) | Get only unresolved/active comments |
| Comments | [reply_to_pull_request_comment](#reply_to_pull_request_comment) | Reply to an existing comment thread |
| Comments | [add_pull_request_comment](#add_pull_request_comment) | Add a new comment (general or line-specific) |
| Tasks | [get_pull_request_tasks](#get_pull_request_tasks) | List tasks attached to a pull request |
| Tasks | [create_pull_request_task](#create_pull_request_task) | Create a task attached to a comment |
| Tasks | [update_pull_request_task](#update_pull_request_task) | Update task description |
| Tasks | [delete_pull_request_task](#delete_pull_request_task) | Delete a task |
| Builds | [get_build_status](#get_build_status) | Get CI/CD build status for a commit |
| Builds | [get_pull_request_build_status](#get_pull_request_build_status) | Get build status for PR head commit |
| Builds | [list_builds](#list_builds) | 🆕 List recent builds for a repository |
| Integrations | [get_pull_request_jira_issues](#get_pull_request_jira_issues) | List Jira issues linked to a pull request |

> ⭐ **Workflow-Oriented Tools**: These tools are optimized to provide comprehensive context in a single call, reducing the number of tool invocations needed for common tasks.

---

## Projects

### list_projects

List all available Bitbucket projects to discover repositories. Returns cached project metadata including name, key, description, and repository count.

- **Required**: None
- **Optional**: None

---

## Dashboard

Dashboard tools provide a user-centric view of pull requests relevant to the current authenticated user.

### get_my_pull_requests

Get pull requests that the current user has authored, is reviewing, or is participating in. This is useful for tracking your personal workload across all repositories.

- **Required**: None
- **Optional**: 
  - `role` (AUTHOR, REVIEWER, PARTICIPANT; default: AUTHOR) - Filter by your role
  - `state` (OPEN, MERGED, DECLINED, ALL; default: OPEN) - Filter by PR state
  - `limit` (default: 25) - Maximum results to return

### get_inbox_pull_requests

Get pull requests in your review inbox that need your attention. This shows PRs where you are a reviewer and haven't yet completed your review.

- **Required**: None
- **Optional**: 
  - `limit` (default: 25) - Maximum results to return

**Output includes:**
- Total PRs in inbox
- PR details: ID, title, repository, author
- Reviewer status for each PR

### get_recent_repositories

Get repositories the current user has recently accessed. Returns cached repository metadata from the user's recent activity.

- **Required**: None
- **Optional**: None

**Output includes:**
- Project key and repository slug
- Repository name

### get_server_info

Get information about the Bitbucket Server instance including version, build number, current user, and cache statistics.

- **Required**: None
- **Optional**: None

**Output includes:**
- Server version, display name, and build number
- Current authenticated user details (name, username, slug, email)
- Cache statistics: project count, repository count, recent repositories count

### get_current_user

Get information about the currently authenticated user.

- **Required**: None
- **Optional**: None

**Output includes:**
- Display name and username
- User slug (for use with other tools)
- Email address
- Active status
- User type

---

## Repositories

### list_repositories

List repositories within a specific Bitbucket project.

- **Required**: `projectKey`
- **Optional**: 
  - `minimalOutput` (default: false) - 🆕 Return compact format with repository slugs only

### get_repository_overview

⭐ **Workflow-Oriented Tool**

Get comprehensive repository information including default branch, recent branches, tags, and open pull requests in a single call. Use this to quickly understand a repository's state when exploring a new codebase.

- **Required**: `projectKey`, `repositorySlug`
- **Optional**: 
  - `branchLimit` (default: 10) - Maximum branches to include
  - `tagLimit` (default: 5) - Maximum tags to include
  - `includeOpenPRs` (default: true) - Include recent open pull requests
  - `prLimit` (default: 5) - Maximum open PRs to include

**Output includes:**
- Repository name, visibility, SCM type
- Clone URLs (SSH and HTTPS)
- Default branch
- Recent branches with latest commit
- Recent tags
- Open pull requests summary

**Use this instead of:** Multiple calls to `list_branches`, `list_tags`, and `list_pull_requests`

### get_file_content

Get the raw content of a file in a repository at a specific commit or branch.

- **Required**: `projectKey`, `repositorySlug`, `filePath`
- **Optional**: `at` (commit hash or branch name)

---

## Git

### list_branches

List all branches in a repository to find feature or release branches.

- **Required**: `projectKey`, `repositorySlug`
- **Optional**: 
  - `filter` - Filter by branch name
  - `baseRef` - Base branch or tag
  - `limit` (default: 25)
  - `minimalOutput` (default: false) - 🆕 Return compact format with branch names only

### list_tags

List tags to identify release points in a repository.

- **Required**: `projectKey`, `repositorySlug`
- **Optional**: `filter`, `limit` (default: 50)

### list_files

List files in a repository at a specific commit or branch.

- **Required**: `projectKey`, `repositorySlug`
- **Optional**: `ref`, `limit` (default: 200)

---

## Search

### search_code

Search for code patterns in repository files. Performs a grep-style search through files and returns matching lines with context.

- **Required**: `projectKey`, `repositorySlug`, `query`
- **Optional**: 
  - `pathPattern` - Glob pattern to filter files (e.g., '*.cs', 'src/**/*.ts')
  - `at` - Branch or commit to search (default: repository's default branch)
  - `isRegex` (default: false) - Use regex matching instead of literal text
  - `caseSensitive` (default: false) - Case-sensitive search
  - `limit` (default: 30, max: 100) - Maximum results to return

**Output includes:**
- File path and line number for each match
- Matched line with context lines before and after
- Search statistics (files scanned, skipped)

**Note:** For large repositories, this may be slow as it scans files individually. Use `pathPattern` to narrow the search scope.

### search_commits

🆕 Search commit history by message text, author, and/or date range. Can search a single repository or all repositories in a project.

- **Required**: `projectKey`
- **Optional**: 
  - `repositorySlug` - Specific repository (omit to search all repos in project)
  - `messageContains` - Text to match in commit messages (case-insensitive)
  - `author` - Author name or email to filter by (partial match)
  - `fromRef` - Branch or commit to start search from
  - `since` - Only commits after this date (ISO 8601 format, e.g., '2024-01-15')
  - `until` - Only commits before this date (ISO 8601 format)
  - `limit` (default: 50, max: 500) - Maximum results to return

**Output includes:**
- Commit hash (short and full)
- Author name and email
- Commit timestamp
- Commit message (truncated for readability)

**Use cases:**
- Find commits mentioning a bug or feature: `messageContains="JIRA-123"`
- Find recent commits by a specific author: `author="jdoe" since="2024-01-01"`
- Audit commits across a project: omit `repositorySlug` for cross-repo search

### search_pull_requests

🆕 Search pull requests by title, description, author, or state across a repository or project.

- **Required**: `projectKey`
- **Optional**: 
  - `repositorySlug` - Specific repository (omit to search all repos in project)
  - `textContains` - Text to match in PR title or description (case-insensitive)
  - `author` - Author username or display name to filter by (partial match)
  - `state` (OPEN, MERGED, DECLINED, ALL; default: ALL) - Filter by PR state
  - `targetBranch` - Filter by target branch name (e.g., 'main')
  - `sourceBranch` - Filter by source branch name
  - `limit` (default: 25, max: 100) - Maximum results to return

**Output includes:**
- PR ID, title, and state (with emoji indicators)
- Author name
- Source and target branches
- Last updated timestamp
- Reviewer count
- Link to PR (when available)

**Use cases:**
- Find PRs targeting a release branch: `targetBranch="release/1.0"`
- Find your PRs across a project: `author="jdoe"` (omit repositorySlug)
- Review merged PRs for a feature: `textContains="payment" state="MERGED"`

### search_users

🆕 Search for Bitbucket users by name, username, or email. Returns user slugs that can be used with other tools.

- **Required**: `query` - Search query (matches name, username, or email)
- **Optional**: 
  - `limit` (default: 25, max: 100) - Maximum results to return

**Output includes:**
- User slug (unique identifier for use with other tools)
- Display name
- Email address
- Active/inactive status

**Use cases:**
- Find reviewers to add to a pull request
- Look up user information for mentions
- Verify user slugs before using with other tools

**Note:** Access to user search may require appropriate permissions depending on Bitbucket Server configuration. A 403 error indicates insufficient permissions.

---

## History

### list_commits

🆕 List commits in a repository. Returns commit history for a branch, tag, or from a specific commit.

- **Required**: `projectKey`, `repositorySlug`
- **Optional**:
  - `ref` (default: repository's default branch) - Branch name, tag, or commit hash to start listing from
  - `path` - Filter to commits affecting a specific file or directory
  - `merges` (include, exclude, only; default: include) - How to handle merge commits
  - `limit` (default: 25, max: 100) - Maximum number of commits to return

**Output includes:**
- Short commit hash
- Author name
- Date
- First line of commit message

### get_commit

Get details of a specific commit including author, message, and parent commits.

- **Required**: `projectKey`, `repositorySlug`, `commitId`
- **Optional**: None

### get_commit_changes

View the files changed in a specific commit.

- **Required**: `projectKey`, `repositorySlug`, `commitId`
- **Optional**: `limit` (default: 100)

### get_commit_context

⭐ **Workflow-Oriented Tool**

Get complete commit details including metadata, changed files, and optionally the full diff in a single call. Use this to understand a commit's full impact when investigating changes.

- **Required**: `projectKey`, `repositorySlug`, `commitId`
- **Optional**: 
  - `includeDiff` (default: false) - Include file-level diff content for detailed code review
  - `contextLines` (default: 3) - Number of context lines around changes when includeDiff is true

**Output includes:**
- Full commit hash and short hash
- Author and committer information with timestamps
- Parent commit references
- Complete commit message
- Changed files grouped by type (ADD, DELETE, MODIFY, MOVE, COPY)
- Optional: Full diff content

**Use this instead of:** Sequential calls to `get_commit` and `get_commit_changes`

### compare_refs

Compare the diff between two refs (branches, tags, or commits) in a repository. Shows what the `from` ref has that the `to` ref does not.

- **Required**: `projectKey`, `repositorySlug`, `from`, `to`
- **Optional**: 
  - `srcPath` - Limit diff to a specific file or directory path
  - `contextLines` (default: 3) - Number of context lines around each change

**Use cases:**
- Compare a feature branch against main: `from="feature/xyz"`, `to="main"`
- View changes between tags: `from="v2.0"`, `to="v1.0"`
- Diff a specific file: `from="feature/xyz"`, `to="main"`, `srcPath="src/app.ts"`

---

## Pull Requests

### list_pull_requests

List pull requests for a repository with optional filtering by state.

- **Required**: `projectKey`, `repositorySlug`
- **Optional**: 
  - `state` (OPEN, MERGED, DECLINED, ALL; default: OPEN)
  - `limit` (default: 25)
  - `minimalOutput` (default: false) - 🆕 Return compact format (PR#, title, state, author)

### get_pull_request

Get detailed metadata for a specific pull request including title, description, author, reviewers, and links.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: None

### get_pull_request_context

⭐ **Workflow-Oriented Tool**

Get complete pull request details including metadata, reviewers, tasks, comments, activity timeline, and diff summary in a single call. This is the recommended tool for comprehensive PR review context.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: 
  - `includeComments` (default: true) - Include comments and discussions
  - `includeDiff` (default: false) - Include diff/changes summary for code review
  - `includeActivity` (default: false) - Include activity timeline (approvals, updates)
  - `includeTasks` (default: true) - Include tasks attached to the PR

**Output includes:**
- PR metadata: title, state, author, dates, source/target branches
- Description
- Reviewers with approval status (✅ Approved, 🔧 Needs Work, ⏳ Pending)
- Tasks with state indicators (🔴 open, ✅ resolved)
- Activity timeline (when enabled): approvals, updates, merges
- Comments with file context and reply threads (when enabled)
- Changes summary: files changed, lines added/removed (when enabled)

**Use this instead of:** Multiple calls to `get_pull_request`, `get_pull_request_diff`, `get_pull_request_comments`, and `get_pull_request_tasks`

### get_pull_request_diff

Get the diff text for a specific pull request showing all file changes.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: None

### create_pull_request

Create a new pull request from the source branch into the target branch.

- **Required**: `projectKey`, `repositorySlug`, `title`, `sourceRef`, `targetRef`
- **Optional**: `description`, `reviewers` (comma-separated usernames)

### update_pull_request

Update pull request metadata such as title, description, or reviewers.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: `title`, `description`, `addReviewers`, `removeReviewers`

### approve_pull_request

Approve a pull request.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: None

---

## Comments

### get_pull_request_comments

Get all comments for a specific pull request, including code context and nested replies. Each comment shows the author, date, file location (if applicable), code snippet context, and the full comment thread hierarchy.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: `anchorState` (ACTIVE, ORPHANED, ALL; default: ALL)

**Output includes:**
- Comment ID and metadata (author, dates)
- File path and line number for inline comments
- Surrounding code context for line-specific comments
- Nested reply threads with full hierarchy
- Comment text content

### get_pull_request_unresolved_comments

Get only the unresolved/active comments for a pull request. This filters out resolved discussions and orphaned comments, showing only items that still need attention.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: None

**Use this tool to:**
- Identify outstanding review items
- Find discussions that need responses
- Track remaining work before merge

### reply_to_pull_request_comment

Reply to an existing comment on a pull request. Creates a threaded reply under the specified parent comment, maintaining the conversation context.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`, `parentCommentId`, `text`
- **Optional**: None

### add_pull_request_comment

Add a new comment to a pull request. Can be a general comment on the PR or attached to a specific line in a file.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`, `text`
- **Optional**: `filePath`, `line`, `lineType` (ADDED, REMOVED, CONTEXT; default: ADDED), `fileType` (FROM, TO; auto-derived from lineType if not specified)

**Use cases:**
- General PR feedback: Omit `filePath` and `line`
- Line-specific review: Provide `filePath` and `line`
- Comment on deleted line: Set `lineType="REMOVED"` (fileType auto-set to FROM)
- Explicit file targeting: Set `fileType="FROM"` for source file, `"TO"` for destination file

---

## Tasks

Tasks are actionable items attached to comments on pull requests. They must be resolved before the PR can be merged (depending on repository settings).

### get_pull_request_tasks

List all tasks associated with a pull request. Tasks are displayed grouped by state (open vs resolved).

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: `state` (OPEN, RESOLVED, ALL; default: ALL)

**Output includes:**
- Summary of open and resolved task counts
- Task ID, state, author, creation date
- Task description text
- Anchor information (which comment the task is attached to)

### create_pull_request_task

Create a new task attached to a comment on a pull request. Tasks are used to track required actions that must be completed before merging.

- **Required**: `commentId`, `text`
- **Optional**: 
  - `projectKey` - Bitbucket project key for validation
  - `repositorySlug` - Repository slug for validation
  - `pullRequestId` - Pull request ID for validation

**Note:** Tasks are anchored to specific comments. Use `get_pull_request_comments` to find available comment IDs. When context parameters (`projectKey`, `repositorySlug`, `pullRequestId`) are provided, the tool validates that the comment exists on the specified PR.

### update_pull_request_task

Update the description text of an existing task.

- **Required**: `taskId`, `text`
- **Optional**: None

### delete_pull_request_task

Delete a task from a pull request.

- **Required**: `taskId`
- **Optional**: None

---

## Builds

Build status tools provide visibility into CI/CD pipeline results for commits and pull requests.

### get_build_status

Get build status information for a specific commit. Shows CI/CD pipeline results including build name, state, and links.

- **Required**: `commitId`
- **Optional**: 
  - `includeStats` (default: true) - Include summary statistics
  - `limit` (default: 25) - Maximum build results to return

**Output includes:**
- Overall status (PASSING, FAILING, IN PROGRESS, NO BUILDS)
- Summary: successful, failed, in-progress counts
- Detailed builds grouped by state with:
  - Build name and description
  - Link to build details
  - Timestamp

### get_pull_request_build_status

Get build status for the head commit of a pull request. This is a convenience tool that finds the latest commit in the PR and returns its build status.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: None

**Use this to:**
- Check if PR builds are passing before review
- Verify CI/CD status before merging
- Debug failing builds on a PR

### list_builds

🆕 List recent builds for a repository. Shows CI/CD pipeline history across commits on a branch.

- **Required**: `projectKey`, `repositorySlug`
- **Optional**: 
  - `branch` - Branch name to get builds for (default: repository's default branch)
  - `limit` (default: 25, max: 100) - Maximum number of commits to check for builds

**Output includes:**
- Summary of build health across analyzed commits
- Overall status (ALL PASSING, SOME FAILING, BUILDS IN PROGRESS, NO BUILDS)
- For each commit:
  - Short commit hash and message
  - Author name and commit date
  - Build status counts (✅ successful, ❌ failed, ⏳ in progress)

**Use this to:**
- Monitor build stability over time
- Identify which commits broke the build
- Track CI/CD health for a branch

---

## Integrations

### get_pull_request_jira_issues

List Jira issues linked to a specific pull request. Useful for tracing work items associated with code changes.

- **Required**: `projectKey`, `repositorySlug`, `pullRequestId`
- **Optional**: None
