using ModelContextProtocol.Protocol;

namespace McpServerFactory.Testing;

public sealed record McpServerFactoryOptions
{
    public Implementation ServerInfo { get; init; } = new()
    {
        Name = "TestMcpServer",
        Version = "1.0.0",
    };

    public TimeSpan InitializationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public string? ServerInstructions { get; init; }
}
