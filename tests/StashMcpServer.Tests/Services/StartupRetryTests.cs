using Bitbucket.Net.Common.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using StashMcpServer.Services;
using System.Net;

namespace StashMcpServer.Tests.Services;

public class StartupRetryTests
{
    private static readonly Func<int, TimeSpan> NoDelay = _ => TimeSpan.Zero;

    [Fact]
    public async Task ValidateWithRetryAsync_TransientThenSuccess_RetriesUntilSuccess()
    {
        var calls = 0;

        await StartupRetry.ValidateWithRetryAsync(
            _ =>
            {
                calls++;
                if (calls < 3)
                {
                    throw new HttpRequestException("connection refused");
                }

                return Task.CompletedTask;
            },
            maxAttempts: 3,
            NoDelay,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ValidateWithRetryAsync_NonTransient_FailsImmediatelyWithoutRetry()
    {
        var calls = 0;

        await Assert.ThrowsAsync<BitbucketForbiddenException>(() =>
            StartupRetry.ValidateWithRetryAsync(
                _ =>
                {
                    calls++;
                    throw new BitbucketForbiddenException("bad token", []);
                },
                maxAttempts: 3,
                NoDelay,
                NullLogger.Instance,
                CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ValidateWithRetryAsync_TransientExhausted_ThrowsAfterMaxAttempts()
    {
        var calls = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            StartupRetry.ValidateWithRetryAsync(
                _ =>
                {
                    calls++;
                    throw new HttpRequestException("still down");
                },
                maxAttempts: 3,
                NoDelay,
                NullLogger.Instance,
                CancellationToken.None));

        Assert.Equal(3, calls);
    }

    [Fact]
    public void IsTransient_ClassifiesCorrectly()
    {
        Assert.True(StartupRetry.IsTransient(new HttpRequestException("net")));
        Assert.True(StartupRetry.IsTransient(new BitbucketServerException("5xx", HttpStatusCode.InternalServerError, [])));
        Assert.False(StartupRetry.IsTransient(new BitbucketForbiddenException("403", [])));
        Assert.False(StartupRetry.IsTransient(new BitbucketNotFoundException("404", [])));
    }
}
