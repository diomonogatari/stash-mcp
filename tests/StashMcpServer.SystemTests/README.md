# StashMcpServer.SystemTests

End-to-end tests that boot a **real, ephemeral Bitbucket Data Center instance** in a
[Testcontainers](https://dotnet.testcontainers.org/) container, seed it with a deterministic
fixture, and drive the actual MCP tools against it — no mocks at the HTTP boundary.

Two layers are covered:

| File | What it exercises |
|------|-------------------|
| [`LivePipelineE2ETests`](LivePipelineE2ETests.cs) | The real tool classes resolved from the real DI pipeline (cache → resilience → HTTP client), invoked directly against the live server. |
| [`McpTransportE2ETests`](McpTransportE2ETests.cs) | The same tools driven through a real MCP client over the in-memory transport — the way a model calls them. |

## These tests skip by default

They run **only** when a Bitbucket Data Center license is supplied via the `STASH_TEST_LICENSE`
environment variable (and Docker is reachable). Without it, every test is **skipped**, so a plain
`dotnet test` stays green on any machine and in CI.

```
Skipped! - Failed: 0, Passed: 0, Skipped: 12
```

## Why a license is needed (and how to get one for free)

Bitbucket Data Center will not start past setup without a license, and as of **30 March 2026**
Atlassian no longer issues self-serve evaluation licenses for its own Data Center products. The
free, repeatable path is a **timebomb license** — a temporary license Atlassian publishes
specifically for testing:

- Atlassian's published timebomb licenses:
  <https://developer.atlassian.com/platform/marketplace/timebomb-licenses-for-testing-server-apps/>
- A timebomb license expires ~3 hours after the instance **starts** (not on a calendar date), which
  is irrelevant here — each test container lives for minutes and is thrown away.

On that page, use the entry titled **"10 user Bitbucket Data Center license, expires in 3 hours"**
(the generic "general testing" license is for a different product and Bitbucket rejects it). Put the
string in `STASH_TEST_LICENSE`; it is never committed to this repo.

> **Copy it exactly.** The page wraps the key across several lines; join them into one unbroken
> string with no spaces. A single altered character makes Bitbucket reject it
> (`setup.license ... is not valid`). Tools that "summarize" or reflow the page can corrupt the
> base64 — copy from the raw page text.

## Running them

```bash
# from the repo root
export STASH_TEST_LICENSE='AAAB...'          # a Bitbucket DC timebomb license string
dotnet test tests/StashMcpServer.SystemTests/StashMcpServer.SystemTests.csproj
```

First run pulls the `atlassian/bitbucket` image (~1 GB) and boots for a few minutes; later runs are
faster. Budget **~2 GB RAM** for the container.

### Environment variables

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `STASH_TEST_LICENSE` | yes | — | Bitbucket DC timebomb license. Absent ⇒ tests skip. |
| `STASH_TEST_BITBUCKET_IMAGE` | no | `atlassian/bitbucket:9.6` | Override the image/tag under test. |

### Running them in VS Code

The C# Dev Kit Test Explorer discovers these tests, but they **skip** unless the test host has
`STASH_TEST_LICENSE` set (and Docker is running) — that's why they show as "not run". Two options:

- **Integrated terminal (quick):** set the variable, then run the `test: system e2e` task (or just
  `STASH_TEST_LICENSE='…' dotnet test tests/StashMcpServer.SystemTests/StashMcpServer.SystemTests.csproj`).
  Tip: launching VS Code from a shell that already `export`ed the variable makes the whole Test
  Explorer pick it up.
- **Test Explorer "Run" (persistent):** copy [`system.runsettings.example`](system.runsettings.example)
  to `system.runsettings` (gitignored), paste your license, and add to your VS Code settings:
  `"dotnet.unitTests.runSettingsPath": "tests/StashMcpServer.SystemTests/system.runsettings"`. Reload
  the window; the tests now run from the gutter/Test Explorer.

> Rebuild after pulling so discovery matches the code (an old `GetCurrentUser_*` test was replaced by
> `GetServerInfo_*`).

## What the harness does

1. **Boots** `atlassian/bitbucket` with fully unattended setup: an entrypoint wrapper writes a
   `bitbucket.properties` (the `setup.*` keys — license, `admin` account, base URL) into the
   container's shared home, chowned to the run user, then hands off to the real entrypoint. This is
   the reliable path — the official image doesn't apply `SETUP_*` env vars, and a plain file-copy
   stays mis-owned. It uses the embedded database (no `JDBC_*`), disables bundled search for speed,
   and waits until `GET /status` reports `RUNNING`. See [`BitbucketContainer`](Infrastructure/BitbucketContainer.cs).
2. **Seeds** a deterministic fixture entirely over REST — no git binary — via
   [`BitbucketSeeder`](Infrastructure/BitbucketSeeder.cs): an HTTP access token (the MCP server's
   bearer token), a project, a repo with two commits on `main`, a tag, a `feature/demo` branch with
   its own change, an open pull request with a comment and a task, and a build status.
3. **Wires** the MCP server's real composition root at the live container and **warms the cache**
   deterministically (the production `StartupService` warms it on a background task that would race
   the tests). See [`LiveStashMcpFactory`](Infrastructure/LiveStashMcpFactory.cs) and
   [`LiveComposition`](Infrastructure/LiveComposition.cs).

The container is shared across the whole test collection (booted once) by
[`BitbucketServerFixture`](Infrastructure/BitbucketServerFixture.cs).

## Notes

- On Bitbucket 9.x the legacy pull-request *tasks* endpoint is gone, so `get_pull_request_tasks`
  takes its graceful-degradation path — `GetPullRequestTasks_RunsAgainstLiveServer` asserts that
  real behavior rather than expecting task data the server can't return.
- Prefer pinning `STASH_TEST_BITBUCKET_IMAGE` to a specific patch tag (e.g.
  `atlassian/bitbucket:9.6.2`) for reproducibility.
- To poke around a Bitbucket instance by hand instead of through the tests, see
  [`docs/local-bitbucket.md`](../../docs/local-bitbucket.md).
