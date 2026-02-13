## McpServerFactory

`McpServerFactory` provides in-memory integration testing primitives for
.NET MCP servers, similar in spirit to `WebApplicationFactory<T>` for
ASP.NET Core APIs.

It starts an MCP server in-process, connects an `McpClient` through
stream pipes, and lets tests call tools directly with zero network setup.

## Why this exists

- MCP servers lacked a reusable .NET integration test factory.
- SDK test internals are not exposed as a dedicated testing package.
- stash-mcp needed realistic integration tests beyond unit-level mocks.

## What it gives you

- In-process MCP server host (`Host.CreateApplicationBuilder`)
- In-memory duplex transport wiring (`Pipe` + stream transports)
- `McpClient` creation and lifecycle management
- DI override hooks for mocked dependencies
- Optional helper wrapper (`McpTestClient`)

## Quick start (inline pattern)

```csharp
using McpServerFactory.Testing;
using StashMcpServer.Tools;

await using var factory = new McpServerFactory(
    configureServices: services =>
    {
        services.AddSingleton(Substitute.For<IBitbucketClient>());
        services.AddSingleton(Substitute.For<IBitbucketCacheService>());
        services.AddSingleton(Substitute.For<IResilientApiService>());
        services.AddSingleton(Substitute.For<IServerSettings>());
        services.AddSingleton(Substitute.For<IDiffFormatter>());
    },
    configureMcpServer: mcp =>
    {
        mcp.WithToolsFromAssembly(typeof(ProjectTools).Assembly);
    });

await using var client = await factory.CreateClientAsync();
var tools = await client.ListToolsAsync();
```

## Quick start (subclass pattern)

```csharp
using McpServerFactory.Testing;

public sealed class MyTestFactory : McpServerFactory
{
    protected override void ConfigureMcpServer(IMcpServerBuilder builder)
    {
        builder.WithToolsFromAssembly(typeof(ProjectTools).Assembly);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Substitute.For<IBitbucketClient>());
        // ...register remaining test doubles
    }
}
```

## API at a glance

- `McpServerFactory`
  - `CreateClientAsync()` starts host + returns connected client
  - `Services` gives access to DI container after startup
  - Override `ConfigureMcpServer` and `ConfigureServices` for custom setup
- `McpServerFactoryOptions`
  - Server info, initialization timeout, optional server instructions
- `McpTestClient`
  - Convenience helper to list tool names and extract text responses

## WebApplicationFactory analogy

- `WebApplicationFactory<T>` boots a TestServer and returns `HttpClient`
- `McpServerFactory` boots an MCP host and returns `McpClient`
- `ConfigureWebHost` maps conceptually to:
  - `ConfigureMcpServer` (tool registration)
  - `ConfigureServices` (dependency overrides)

## Notes

- The current namespace is `McpServerFactory.Testing`.
- This package intentionally has no dependency on a specific test framework.
- Tests call MCP tools directly, so no LLM/token usage is required.
