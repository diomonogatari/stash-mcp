---
description: "Use when working on stash-mcp codebase structure, project references, build setup, Docker, CI workflows, or the Bitbucket.Net submodule. Covers repository layout, submodule conventions, and build/test commands."
applyTo: "**/*.csproj,**/*.slnx,**/Dockerfile,**/*.yml,**/.gitmodules"
---

# Stash MCP Server — Codebase Overview

## What This Project Is

Stash MCP Server is a .NET 10.0 Model Context Protocol (MCP) server for Atlassian Bitbucket Server (Stash). It exposes tools that let AI assistants interact with repositories, pull requests, code reviews, builds, and search.

## Repository Layout

```
stash-mcp/
├── src/StashMcpServer/          # MCP server application (Microsoft.NET.Sdk.Web)
├── tests/StashMcpServer.Tests/  # xUnit test project
├── lib/Bitbucket.Net/           # ⚠️ Git submodule — Bitbucket REST API client
├── docs/                        # Architecture and toolset documentation
├── .github/workflows/           # CI: build.yml + release.yml
├── Directory.Build.props        # Shared MSBuild properties
├── Directory.Packages.props     # Central Package Management (CPM)
└── Dockerfile                   # Multi-stage Docker build
```

## Git Submodule — Bitbucket.Net

The Bitbucket REST API client lives in `lib/Bitbucket.Net/` as a **git submodule** pointing to `https://github.com/diomonogatari/Bitbucket.Net.git`. We own both repositories.

### Key rules

- **StashMcpServer references Bitbucket.Net via ProjectReference**, not NuGet:
  `<ProjectReference Include="..\..\lib\Bitbucket.Net\src\Bitbucket.Net\Bitbucket.Net.csproj" />`
- **Bitbucket.Net manages its own package versions.** It has a `Directory.Packages.props` that opts out of CPM (`ManagePackageVersionsCentrally=false`), so stash-mcp's CPM does not conflict.
- **Never add Bitbucket.Net packages to stash-mcp's `Directory.Packages.props`.** Bitbucket.Net's dependencies (e.g., Flurl.Http) are declared in its own `.csproj`.
- **Cloning requires `--recurse-submodules`:**
  ```bash
  git clone --recurse-submodules https://github.com/diomonogatari/stash-mcp.git
  # or, after a plain clone:
  git submodule update --init --recursive
  ```
- **CI workflows use `submodules: recursive`** on all `actions/checkout@v4` steps.
- **Dockerfile copies submodule source** in the build stage — both the `.csproj` (for restore layer caching) and the full source.
