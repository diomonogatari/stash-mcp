using Serilog.Events;

namespace StashMcpServer.Configuration;

/// <summary>
/// Parses command-line arguments and maps log levels between Serilog and Microsoft.Extensions.Logging.
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// Parses command-line arguments, falling back to environment variable defaults.
    /// </summary>
    public static (string? StashUrl, string? Pat, LogEventLevel LogLevel) ParseArguments(
        string[] args,
        string? defaultStashUrl,
        string? defaultPat)
    {
        var stashUrl = defaultStashUrl;
        var pat = defaultPat;
        var logLevel = LogEventLevel.Information;

        var index = 0;
        while (index < args.Length)
        {
            var arg = args[index];

            switch (arg)
            {
                case "--stash-url" when index + 1 < args.Length:
                    stashUrl = args[index + 1];
                    index += 2;
                    continue;
                case "--pat" when index + 1 < args.Length:
                    pat = args[index + 1];
                    index += 2;
                    continue;
                case "--log-level" when index + 1 < args.Length:
                    var levelValue = args[index + 1];
                    if (!Enum.TryParse(levelValue, true, out LogEventLevel parsedLevel))
                    {
                        Console.Error.WriteLine($"Warning: Unrecognized log level '{levelValue}'. Defaulting to Information.");
                    }
                    else
                    {
                        logLevel = parsedLevel;
                    }

                    index += 2;
                    continue;
            }

            index++;
        }

        return (stashUrl, pat, logLevel);
    }

    /// <summary>
    /// Maps a Serilog <see cref="LogEventLevel"/> to the equivalent Microsoft <see cref="LogLevel"/>.
    /// </summary>
    public static LogLevel MapToMicrosoftLogLevel(LogEventLevel serilogLevel) => serilogLevel switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Critical,
        _ => LogLevel.Information,
    };
}