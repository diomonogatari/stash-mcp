using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace StashMcpServer.SystemTests.Infrastructure;

/// <summary>
/// Collection fixture that boots one ephemeral Bitbucket container for the whole test collection and
/// seeds it once. Disabled (tests skip) unless a timebomb license is supplied via
/// <c>STASH_TEST_LICENSE</c> — booting still requires a reachable Docker daemon.
/// </summary>
public sealed class BitbucketServerFixture : IAsyncLifetime
{
    private IContainer? _container;
    private ServiceProvider? _pipeline;

    /// <summary>True once the container is up and seeded; gates every test via <c>Skip.IfNot</c>.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason shown when the suite is skipped.</summary>
    public string SkipReason { get; private set; } =
        "Set STASH_TEST_LICENSE to a Bitbucket Data Center timebomb license (and ensure Docker is running) " +
        "to run the end-to-end tests. See tests/StashMcpServer.SystemTests/README.md.";

    /// <summary>Base URL of the running container (valid only when <see cref="IsAvailable"/>).</summary>
    public Uri BaseUrl { get; private set; } = new("http://localhost/");

    /// <summary>The seeded fixtures and the MCP access token (valid only when <see cref="IsAvailable"/>).</summary>
    public SeededData Seeded { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var license = Environment.GetEnvironmentVariable("STASH_TEST_LICENSE");
        if (string.IsNullOrWhiteSpace(license))
        {
            return;
        }

        // Container pull + boot + automated setup + seeding. Generous by default for slower CI
        // runners and cold image pulls; overridable via STASH_TEST_STARTUP_TIMEOUT_MINUTES.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(ResolveStartupTimeoutMinutes()));

        _container = BitbucketContainer.Build(license);
        await _container.StartAsync(cts.Token).ConfigureAwait(false);

        BaseUrl = BitbucketContainer.BaseUrl(_container);
        Seeded = await new BitbucketSeeder(BaseUrl).SeedAsync(cts.Token).ConfigureAwait(false);
        IsAvailable = true;
    }

    private static double ResolveStartupTimeoutMinutes()
    {
        const double defaultMinutes = 10;
        var configured = Environment.GetEnvironmentVariable("STASH_TEST_STARTUP_TIMEOUT_MINUTES");
        return double.TryParse(configured, System.Globalization.CultureInfo.InvariantCulture, out var minutes) && minutes > 0
            ? minutes
            : defaultMinutes;
    }

    /// <summary>Creates a fresh in-process MCP server+client wired to the live container.</summary>
    public LiveStashMcpFactory CreateFactory() => new(BaseUrl, Seeded.AccessToken);

    /// <summary>
    /// The real tool pipeline (no MCP transport) with a pre-warmed cache, built once and shared.
    /// Safe because xUnit runs the tests within a collection sequentially.
    /// </summary>
    public async Task<ServiceProvider> GetPipelineAsync(CancellationToken cancellationToken) =>
        _pipeline ??= await LiveComposition.BuildAsync(BaseUrl, Seeded.AccessToken, cancellationToken).ConfigureAwait(false);

    public async Task DisposeAsync()
    {
        if (_pipeline is not null)
        {
            await _pipeline.DisposeAsync().ConfigureAwait(false);
        }

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}