using ModelContextProtocol.Protocol;
using Serilog;
using StashMcpServer.Configuration;
using StashMcpServer.Extensions;
using StashMcpServer.Services;

// Parse command-line arguments and environment variables
var stashUrl = Environment.GetEnvironmentVariable("BITBUCKET_URL");
var pat = Environment.GetEnvironmentVariable("BITBUCKET_TOKEN");

var (parsedUrl, parsedPat, logLevel, transport) = CommandLineParser.ParseArguments(args, stashUrl, pat);
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

if (string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
{
    // HTTP transport mode — for Docker / remote deployments
    var builder = WebApplication.CreateBuilder(args);

    ConfigureMcpServer(builder.Services)
        .WithHttpTransport()
        .WithToolsFromAssembly();

    ConfigureServices(builder.Services, builder.Logging, isStdio: false);

    var app = builder.Build();
    app.MapMcp();

    await Console.Error.WriteLineAsync($"Stash MCP Server v{assemblyVersion} listening on HTTP (port 8080, endpoint /)")
        .ConfigureAwait(false);

    await app.RunAsync().ConfigureAwait(false);
}
else
{
    // Stdio transport mode — for local MCP clients (default)
    var builder = Host.CreateApplicationBuilder(args);

    ConfigureMcpServer(builder.Services)
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    ConfigureServices(builder.Services, builder.Logging, isStdio: true);

    var host = builder.Build();
    await host.RunAsync().ConfigureAwait(false);
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
        options.InitializationTimeout = TimeSpan.FromSeconds(30);
    });
}

void ConfigureServices(IServiceCollection services, ILoggingBuilder logging, bool isStdio)
{
    services.AddMcpLogging(logLevel, enableMcpLogDispatcher: isStdio);
    logging.ClearProviders();
    logging.SetMinimumLevel(CommandLineParser.MapToMicrosoftLogLevel(logLevel));
    logging.AddSerilog(dispose: true);

    services.AddBitbucketServices(stashUrl!, pat!);
    services.AddHostedService<StartupService>();
}