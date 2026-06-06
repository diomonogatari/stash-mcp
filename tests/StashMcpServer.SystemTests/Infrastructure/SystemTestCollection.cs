namespace StashMcpServer.SystemTests.Infrastructure;

/// <summary>
/// Binds <see cref="BitbucketServerFixture"/> to a single xUnit collection so the container is
/// booted and seeded exactly once and shared by every end-to-end test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SystemTestCollection : ICollectionFixture<BitbucketServerFixture>
{
    public const string Name = "bitbucket-system";
}