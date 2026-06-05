using Bitbucket.Net.Common.Exceptions;

namespace StashMcpServer.Services;

/// <summary>
/// Bounded retry for the startup Bitbucket connection check. Transient failures
/// (network, 5xx, rate limiting) are retried with caller-supplied backoff so a brief
/// blip at boot does not take the server down; auth/permission failures (e.g. a bad
/// token) fail immediately. If all attempts are exhausted the last exception propagates
/// so the host can decide to fail fast.
/// </summary>
public static class StartupRetry
{
    /// <summary>
    /// Runs <paramref name="probe"/>, retrying transient failures up to
    /// <paramref name="maxAttempts"/> times with the supplied <paramref name="backoff"/>.
    /// </summary>
    public static async Task ValidateWithRetryAsync(
        Func<CancellationToken, Task> probe,
        int maxAttempts,
        Func<int, TimeSpan> backoff,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(backoff);
        ArgumentNullException.ThrowIfNull(logger);

        var attempts = Math.Max(1, maxAttempts);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await probe(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // genuine shutdown — let the caller treat it as normal
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < attempts)
            {
                var delay = backoff(attempt);
                logger.LogWarning(
                    "Bitbucket connection attempt {Attempt}/{MaxAttempts} failed ({Error}). Retrying in {Delay}s...",
                    attempt,
                    attempts,
                    ex.Message,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            // Non-transient failures, and transient failures on the final attempt,
            // are not caught here and propagate to the caller (which fails fast).
        }
    }

    /// <summary>
    /// Whether an exception represents a transient failure worth retrying at startup.
    /// Network errors, server (5xx) errors, and rate limiting are transient; auth and
    /// permission errors are not (retrying a bad token never succeeds).
    /// </summary>
    public static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or BitbucketServerException or BitbucketRateLimitException;
}