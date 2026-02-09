using Serilog.Core;
using Serilog.Events;

namespace StashMcpServer.Logging;

internal sealed class McpSerilogSink(McpLogQueue queue) : ILogEventSink
{
    private readonly McpLogQueue logQueue = queue ?? throw new ArgumentNullException(nameof(queue));

    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
        {
            return;
        }

        logQueue.Enqueue(logEvent);
    }
}