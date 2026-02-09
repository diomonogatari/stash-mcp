using ModelContextProtocol.Server;
using Serilog.Events;

namespace StashMcpServer.Logging;

internal sealed class McpLogDispatcher(
    McpServer mcpServer,
    McpLogQueue logQueue,
    ILogger<McpLogDispatcher> diagnosticLogger) : BackgroundService
{
    private readonly McpServer server = mcpServer ?? throw new ArgumentNullException(nameof(mcpServer));
    private readonly McpLogQueue queue = logQueue ?? throw new ArgumentNullException(nameof(logQueue));
    private readonly ILogger<McpLogDispatcher> logger = diagnosticLogger ?? throw new ArgumentNullException(nameof(diagnosticLogger));

    /// <summary>
    /// Maximum time to wait for the client to set a logging level before starting to forward logs.
    /// MCP clients typically send logging/setLevel immediately after initialize.
    /// </summary>
    private static readonly TimeSpan LoggingLevelWaitTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until the MCP client has set a logging level.
        // Without this, logs are dropped because IsEnabled returns false when LoggingLevel is null.
        await WaitForLoggingLevelAsync(stoppingToken).ConfigureAwait(false);

        using var provider = server.AsClientLoggerProvider();
        using var factory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(provider);
        });

        var targetLogger = factory.CreateLogger("McpSerilogBridge");

        await foreach (var logEvent in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                ForwardToMcp(targetLogger, logEvent);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to forward log entry to MCP transport.");
            }
        }
    }

    private async Task WaitForLoggingLevelAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(LoggingLevelWaitTimeout);

        try
        {
            // Poll until LoggingLevel is set by the client
            while (server.LoggingLevel is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout waiting for client to set logging level - proceed anyway
            // Logs will be dropped until client sets a level, but at least we don't block forever
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        return base.StopAsync(cancellationToken);
    }

    private static void ForwardToMcp(ILogger targetLogger, LogEvent logEvent)
    {
        var logLevel = TranslateLevel(logEvent.Level);
        var eventId = ExtractEventId(logEvent);
        var message = logEvent.RenderMessage(null);

        IDisposable? scope = null;
        if (logEvent.Properties.Count > 0)
        {
            scope = targetLogger.BeginScope(CreateScope(logEvent));
        }

        using (scope)
        {
            targetLogger.Log(logLevel, eventId, logEvent.Exception, "{Message}", message);
        }
    }

    private static IReadOnlyDictionary<string, object?> CreateScope(LogEvent logEvent)
    {
        var scope = new Dictionary<string, object?>(logEvent.Properties.Count, StringComparer.Ordinal);
        foreach (var property in logEvent.Properties)
        {
            scope[property.Key] = ConvertPropertyValue(property.Value);
        }

        return scope;
    }

    private static object? ConvertPropertyValue(LogEventPropertyValue value) => value switch
    {
        ScalarValue scalar => scalar.Value,
        SequenceValue sequence => sequence.Elements.Select(ConvertPropertyValue).ToArray(),
        StructureValue structure => structure.Properties.ToDictionary(p => p.Name, p => ConvertPropertyValue(p.Value)),
        DictionaryValue dictionary => dictionary.Elements.ToDictionary(
            kvp => kvp.Key.Value?.ToString() ?? string.Empty,
            kvp => ConvertPropertyValue(kvp.Value)),
        _ => value.ToString()
    };

    private static EventId ExtractEventId(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("EventId", out var rawEventId) &&
            rawEventId is ScalarValue { Value: int eventIdValue })
        {
            return new EventId(eventIdValue);
        }

        return default;
    }

    private static LogLevel TranslateLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Critical,
        _ => LogLevel.Information
    };
}