# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project infrastructure files first (for layer caching)
COPY global.json Directory.Build.props Directory.Packages.props nuget.config .editorconfig stash-mcp.slnx ./
COPY src/StashMcpServer/StashMcpServer.csproj src/StashMcpServer/

# Copy submodule project files and infrastructure for restore
COPY lib/Bitbucket.Net/Directory.Build.props lib/Bitbucket.Net/
COPY lib/Bitbucket.Net/Directory.Packages.props lib/Bitbucket.Net/
COPY lib/Bitbucket.Net/src/Bitbucket.Net/Bitbucket.Net.csproj lib/Bitbucket.Net/src/Bitbucket.Net/

# Restore as a separate layer
RUN dotnet restore src/StashMcpServer/StashMcpServer.csproj

# Copy remaining source files
COPY src/StashMcpServer/ src/StashMcpServer/
COPY lib/Bitbucket.Net/src/Bitbucket.Net/ lib/Bitbucket.Net/src/Bitbucket.Net/
COPY lib/Bitbucket.Net/.editorconfig lib/Bitbucket.Net/

# Publish as a framework-dependent application (multi-arch via base image)
RUN dotnet publish src/StashMcpServer/StashMcpServer.csproj \
    --configuration Release \
    --output /app \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

LABEL org.opencontainers.image.title="Stash MCP Server" \
      org.opencontainers.image.description="MCP server for Bitbucket Server (Stash) — 40 tools for repositories, PRs, code review, builds, and search" \
      org.opencontainers.image.url="https://github.com/diomonogatari/stash-mcp" \
      org.opencontainers.image.source="https://github.com/diomonogatari/stash-mcp" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app

COPY --from=build /app .

STOPSIGNAL SIGTERM

ENTRYPOINT ["dotnet", "StashMcpServer.dll"]
