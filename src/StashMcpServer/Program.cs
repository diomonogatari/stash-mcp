using ModelContextProtocol.Protocol;
using Serilog;
using StashMcpServer.Configuration;
using StashMcpServer.Extensions;
using StashMcpServer.Services;

// Parse command-line arguments and environment variables
var stashUrl = Environment.GetEnvironmentVariable("BITBUCKET_URL");
var pat = Environment.GetEnvironmentVariable("BITBUCKET_TOKEN");

var (parsedUrl, parsedPat, logLevel) = CommandLineParser.ParseArguments(args, stashUrl, pat);
stashUrl = parsedUrl;
pat = parsedPat;

// Validate required configuration
if (string.IsNullOrEmpty(stashUrl))
{
    await Console.Error.WriteLineAsync("Error: Bitbucket URL is required. Set BITBUCKET_URL environment variable or use --stash-url parameter.")
        .ConfigureAwait(false);
    return 1;
}

if (string.IsNullOrEmpty(pat))
{
    await Console.Error.WriteLineAsync("Error: Personal Access Token is required. Set BITBUCKET_TOKEN environment variable or use --pat parameter.")
        .ConfigureAwait(false);
    return 1;
}

// Derive version from assembly metadata
var assemblyVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

// Redirect Console.Out to prevent any accidental stdout writes from corrupting the JSON-RPC stream. The MCP SDK uses its own stream for protocol messages.
Console.SetOut(TextWriter.Null);

try
{
    var builder = Host.CreateApplicationBuilder(args);

    ConfigureMcpServer(builder.Services)
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    ConfigureServices(builder.Services, builder.Logging);

    var host = builder.Build();
    await host.RunAsync().ConfigureAwait(false);
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

return 0;

IMcpServerBuilder ConfigureMcpServer(IServiceCollection services)
{
    return services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "Bitbucket (Stash) MCP Server",
            Version = assemblyVersion,
        };
        options.ServerInstructions = ServerInstructions.Generate();
        options.InitializationTimeout = TimeSpan.FromSeconds(15);
    });
}

void ConfigureServices(IServiceCollection services, ILoggingBuilder logging)
{
    services.AddMcpLogging(logLevel);
    logging.ClearProviders();
    logging.SetMinimumLevel(CommandLineParser.MapToMicrosoftLogLevel(logLevel));
    logging.AddSerilog(dispose: true);

    services.AddBitbucketServices(stashUrl!, pat!);
    services.AddHostedService<StartupService>();
}