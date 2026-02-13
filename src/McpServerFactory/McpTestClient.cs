using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpServerFactory.Testing;

public sealed class McpTestClient(McpClient client) : IAsyncDisposable
{
    public McpClient Inner { get; } = client;

    public async Task<string[]> GetToolNamesAsync(CancellationToken cancellationToken = default)
    {
        var tools = await Inner
            .ListToolsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return tools.Select(tool => tool.Name).ToArray();
    }

    public async Task<string> CallToolForTextAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Inner
            .CallToolAsync(toolName, arguments: arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        if (textBlock is null)
        {
            throw new InvalidOperationException($"Tool '{toolName}' did not return a text content block.");
        }

        return textBlock.Text;
    }

    public ValueTask DisposeAsync()
    {
        return Inner.DisposeAsync();
    }
}
