# Contributing

Thanks for your interest in contributing to stash-mcp!

## Prerequisites

- Git
- [.NET SDK 10.x](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for image builds)

## Getting Started

Clone the repository **with submodules**:

```bash
git clone --recurse-submodules https://github.com/diomonogatari/stash-mcp.git
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

## Building

```bash
dotnet build stash-mcp.slnx
```

## Tests

Please ensure tests pass before submitting a PR:

```bash
dotnet test stash-mcp.slnx
```

## Docker Image

Build and test the Docker image locally:

```bash
docker build -t diomonogatari/stash-mcp:dev .
```

## Code Style

The project uses `.editorconfig` for formatting rules and
[Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) for
additional code quality checks. All warnings are treated as errors.

## Pull Requests

1. Fork the repository and create a feature branch from `main`.
2. Make your changes with clear, focused commits.
3. Ensure `dotnet build` and `dotnet test` pass with zero warnings.
4. Verify the Docker image builds successfully.
5. Open a pull request against `main`.
