using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpServerFactory.Testing;

public class McpServerFactory(
    Action<IServiceCollection>? configureServices = null,
    Action<IMcpServerBuilder>? configureMcpServer = null,
    McpServerFactoryOptions? options = null) : IAsyncDisposable
{
    private readonly Action<IServiceCollection>? configureServicesAction = configureServices;
    private readonly Action<IMcpServerBuilder>? configureMcpServerAction = configureMcpServer;
    private readonly McpServerFactoryOptions factoryOptions = options ?? new McpServerFactoryOptions();
    private readonly Pipe clientToServerPipe = new();
    private readonly Pipe serverToClientPipe = new();

    private IHost? host;
    private McpClient? client;
    private bool disposed;

    protected McpServerFactory(McpServerFactoryOptions? options = null)
        : this(null, null, options)
    {
    }

    public IServiceProvider Services =>
        host?.Services ?? throw new InvalidOperationException("Call CreateClientAsync before accessing Services.");

    protected virtual void ConfigureMcpServer(IMcpServerBuilder builder)
    {
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected virtual McpClientOptions CreateClientOptions()
    {
        return new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "McpServerFactoryClient",
                Version = "1.0.0",
            },
            InitializationTimeout = factoryOptions.InitializationTimeout,
        };
    }

    public async Task<McpClient> CreateClientAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (client is not null)
        {
            return client;
        }

        var builder = Host.CreateApplicationBuilder();

        var mcpServerBuilder = builder.Services.AddMcpServer(serverOptions =>
        {
            serverOptions.ServerInfo = factoryOptions.ServerInfo;
            serverOptions.ServerInstructions = factoryOptions.ServerInstructions;
            serverOptions.InitializationTimeout = factoryOptions.InitializationTimeout;
        });

        mcpServerBuilder.WithStreamServerTransport(
            clientToServerPipe.Reader.AsStream(),
            serverToClientPipe.Writer.AsStream());

        configureMcpServerAction?.Invoke(mcpServerBuilder);
        ConfigureMcpServer(mcpServerBuilder);

        configureServicesAction?.Invoke(builder.Services);
        ConfigureServices(builder.Services);

        host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        var transport = new StreamClientTransport(
            serverInput: clientToServerPipe.Writer.AsStream(),
            serverOutput: serverToClientPipe.Reader.AsStream());

        client = await McpClient
            .CreateAsync(
                transport,
                clientOptions: CreateClientOptions(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        if (host is not null)
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
        }

        await clientToServerPipe.Writer.CompleteAsync().ConfigureAwait(false);
        await serverToClientPipe.Writer.CompleteAsync().ConfigureAwait(false);
        await clientToServerPipe.Reader.CompleteAsync().ConfigureAwait(false);
        await serverToClientPipe.Reader.CompleteAsync().ConfigureAwait(false);
    }
}
