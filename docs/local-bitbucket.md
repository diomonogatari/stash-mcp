# Running a real Bitbucket Server for testing

`stash-mcp` talks to a self-hosted Bitbucket Data Center (Server / "Stash") instance. You don't need
a paid license to test against a real one — Atlassian publishes free **timebomb licenses** for
exactly this purpose. This page covers both the automated test harness and a by-hand setup.

## The licensing reality

- Bitbucket Data Center won't start past setup without a license.
- As of **30 March 2026**, Atlassian no longer issues self-serve evaluation licenses for its own
  Data Center products, so the old "30-day trial from my.atlassian.com" route is gone.
- **Timebomb licenses** remain free and are the supported way to test. A timebomb license expires
  ~3 hours after the instance *starts* (not on a calendar date) — restarting the container resets
  that clock, so it's effectively unlimited for testing.

Grab the **"10 user Bitbucket Data Center license, expires in 3 hours"** string from Atlassian's
published list (not the generic "general testing" one — Bitbucket rejects that):
<https://developer.atlassian.com/platform/marketplace/timebomb-licenses-for-testing-server-apps/>

Copy it exactly — the page wraps the key across lines; join them into one unbroken string with no
spaces, or Bitbucket reports `setup.license ... is not valid`. Keep it out of source control — pass
it via the `STASH_TEST_LICENSE` environment variable.

## Option A — automated (recommended): the Testcontainers harness

[`tests/StashMcpServer.SystemTests`](../tests/StashMcpServer.SystemTests/README.md) boots an
ephemeral Bitbucket container, seeds it (project, repo, branches, tag, an open PR with a comment and
a task, a build status), and runs the real MCP tools against it. It's fully automated and
deterministic, and it skips cleanly when no license is configured.

```bash
export STASH_TEST_LICENSE='AAAB...'
dotnet test tests/StashMcpServer.SystemTests/StashMcpServer.SystemTests.csproj
```

## Option B — by hand: docker compose

For clicking around the UI or exploring the REST API yourself, use the throwaway compose file:

```bash
export STASH_TEST_LICENSE='AAAB...'
docker compose -f tests/local-bitbucket/docker-compose.yml up
```

When `GET http://localhost:7990/status` returns `{"state":"RUNNING"}`, log in at
<http://localhost:7990> as **admin / admin123**. Tear it down with:

```bash
docker compose -f tests/local-bitbucket/docker-compose.yml down -v
```

### Point stash-mcp at your local instance

1. In Bitbucket: **Profile → Manage account → HTTP access tokens → Create token** (repo read+write).
2. Register the MCP server against it (note `host.docker.internal` so the MCP container can reach the
   Bitbucket container — `localhost` would resolve to the MCP container itself):

```bash
claude mcp add stash-bitbucket --transport stdio \
  --env BITBUCKET_URL=http://host.docker.internal:7990/ \
  --env BITBUCKET_TOKEN=<your_token> \
  -- docker run -i --rm -e BITBUCKET_URL -e BITBUCKET_TOKEN diomonogatari/stash-mcp:latest
```

If you run the MCP server as a plain process (not in a container), use `http://localhost:7990/`
instead.

## Requirements & tips

- Docker 20.10.10+ and ~2 GB RAM free for the container.
- The first image pull is ~1 GB; the first boot takes a few minutes.
- Bundled search (OpenSearch) is disabled in both setups for speed; code-search features won't be
  exercised. Set `SEARCH_ENABLED=true` if you need them (and give the container more memory).
- On Bitbucket 9.x the legacy pull-request *tasks* REST endpoint is removed; `get_pull_request_tasks`
  degrades gracefully rather than erroring.
