using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace StashMcpServer.SystemTests.Infrastructure;

/// <summary>
/// Builds an ephemeral Bitbucket Data Center container configured for fully unattended setup.
///
/// Bitbucket performs its first-run setup from the <c>setup.*</c> keys in
/// <c>$BITBUCKET_HOME/shared/bitbucket.properties</c>. The official <c>atlassian/bitbucket</c> image
/// neither templates <c>SETUP_*</c> environment variables into that file (env vars alone leave the
/// instance parked at <c>FIRST_RUN</c>) nor reliably re-owns files injected under
/// <c>$BITBUCKET_HOME</c> (its recursive chown is skipped when the home dir already looks
/// bitbucket-owned, so a copied-in file stays root/foreign-owned and the app fails to create
/// <c>shared/secured</c>). So instead we wrap the entrypoint: a tiny root shell writes the properties
/// file, chowns it to the run user (uid/gid 2003), then execs the real entrypoint. With the file
/// present and no <c>JDBC_*</c> supplied, the instance boots straight to <c>RUNNING</c> on the
/// embedded database. Bundled search (OpenSearch) is disabled to cut boot time and memory.
/// </summary>
internal static class BitbucketContainer
{
    /// <summary>Container port Bitbucket serves HTTP/REST on.</summary>
    internal const ushort HttpPort = 7990;

    /// <summary>Admin username provisioned by the unattended setup.</summary>
    internal const string AdminUsername = "admin";

    /// <summary>Admin password provisioned by the unattended setup.</summary>
    internal const string AdminPassword = "admin123";

    private const string DefaultImage = "atlassian/bitbucket:9.6";
    private const string PropertiesEnvVar = "STASH_SETUP_PROPERTIES";

    // Runs as root (the image's default user), seeds the setup properties owned by the bitbucket
    // run user (uid/gid 2003), then hands off to the image's real launch: tini -- /entrypoint.py.
    private const string EntrypointWrapper =
        """mkdir -p "$BITBUCKET_HOME/shared" && printf '%s' "$STASH_SETUP_PROPERTIES" > "$BITBUCKET_HOME/shared/bitbucket.properties" && chown -R 2003:2003 "$BITBUCKET_HOME/shared" && exec /usr/bin/tini -- /entrypoint.py""";

    /// <summary>
    /// Builds (but does not start) the container. <paramref name="license"/> is a Bitbucket Data
    /// Center timebomb license — see the project README for where to obtain one.
    /// </summary>
    internal static IContainer Build(string license)
    {
        var image = Environment.GetEnvironmentVariable("STASH_TEST_BITBUCKET_IMAGE");
        if (string.IsNullOrWhiteSpace(image))
        {
            image = DefaultImage;
        }

        return new ContainerBuilder(image)
            .WithPortBinding(HttpPort, assignRandomHostPort: true)
            // Seed automated-setup properties via an entrypoint wrapper so first-run completes
            // with no browser wizard (see the class remarks for why env vars / file copies don't work).
            .WithEnvironment(PropertiesEnvVar, BuildSetupProperties(license))
            .WithEntrypoint("/bin/sh", "-c", EntrypointWrapper)
            // Trim the instance for fast, deterministic boots.
            .WithEnvironment("SEARCH_ENABLED", "false")
            .WithEnvironment("JVM_MINIMUM_MEMORY", "1024m")
            .WithEnvironment("JVM_MAXIMUM_MEMORY", "1024m")
            // /status returns {"state":"STARTING"}/{"state":"FIRST_RUN"} then {"state":"RUNNING"}.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request
                    .ForPort(HttpPort)
                    .ForPath("/status")
                    .ForResponseMessageMatching(IsRunningAsync)))
            .Build();
    }

    /// <summary>Maps a started container to the base URL the MCP server / seeder should target.</summary>
    internal static Uri BaseUrl(IContainer container) =>
        new($"http://{container.Hostname}:{container.GetMappedPublicPort(HttpPort)}/");

    private static string BuildSetupProperties(string license)
    {
        // Strip any whitespace the license picked up from line-wrapping (pasted from the docs page,
        // or a multi-line <STASH_TEST_LICENSE> runsettings element) — base64 has none, so removing
        // it is safe and makes the license forgiving to provide. Backslash is the only character a
        // .properties value must escape; licenses have none, but escape defensively.
        var cleanedLicense = new string(license.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var escapedLicense = cleanedLicense.Replace("\\", "\\\\", StringComparison.Ordinal);

        var lines = new[]
        {
            "setup.displayName=Stash MCP E2E",
            $"setup.baseUrl=http://localhost:{HttpPort}",
            $"setup.license={escapedLicense}",
            $"setup.sysadmin.username={AdminUsername}",
            $"setup.sysadmin.password={AdminPassword}",
            "setup.sysadmin.displayName=E2E Admin",
            "setup.sysadmin.emailAddress=admin@example.com",
        };

        return string.Join('\n', lines) + "\n";
    }

    private static async Task<bool> IsRunningAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return body.Contains("RUNNING", StringComparison.Ordinal);
    }
}