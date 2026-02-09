using Serilog.Events;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace StashMcpServer.Logging;

internal sealed class McpLogQueue
{
    private readonly Channel<LogEvent> channel = Channel.CreateUnbounded<LogEvent>(new UnboundedChannelOptions
    {
        AllowSynchronousContinuations = false,
        SingleReader = true,
        SingleWriter = false
    });

    public void Enqueue(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        channel.Writer.TryWrite(logEvent);
    }

    public async IAsyncEnumerable<LogEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (channel.Reader.TryRead(out var logEvent))
            {
                yield return logEvent;
            }
        }
    }

    public void Complete() => channel.Writer.TryComplete();
}