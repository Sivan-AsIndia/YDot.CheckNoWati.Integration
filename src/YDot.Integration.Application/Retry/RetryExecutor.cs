using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Options;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Retry;

/// <summary>Who a retried call belongs to, for the retry log line and int_job_events.</summary>
/// <param name="JobLabel">"INT-20261010-000001" or "WAT-20261010-000001".</param>
/// <param name="StepCode">See <c>StepCodes</c>.</param>
public sealed record RetryScope(string JobLabel, string StepCode, Guid? TenantId = null, Guid? JobId = null, Guid? WatiJobId = null);

/// <summary>
/// Runs an HTTP call and retries it with exponential back-off when - and only when - the caller's
/// rule says the failure is safe to retry.
///
/// THE DECISION IS THE CALLER'S, because "transient" is not enough on its own: a 504 while
/// polling a task is harmless to repeat, but a 504 while CREATING a billed task may mean the task
/// exists already. So status checks and downloads pass <c>ex =&gt; ex.IsTransient</c>, and
/// billed / message-sending calls pass <c>ex =&gt; ex.IsSafeToRetryWithSideEffects</c>.
///
/// Every retry is logged in the agreed format and recorded in int_job_events:
///   Error "HTTP 503 Service Unavailable" occurred during job INT-20260917-000001 at step
///   WATI_SEND_BATCH. Retry attempt 2/3 after 4 seconds.
/// </summary>
public sealed class RetryExecutor(
    IOptions<RetryOptions> options,
    IJobJournal journal,
    TimeProvider time,
    ILogger<RetryExecutor> logger)
{
    /// <summary>Calls <paramref name="operation"/> with the attempt number (1 = first try).</summary>
    public async Task<T> ExecuteAsync<T>(
        RetryScope scope,
        Func<int, CancellationToken, Task<T>> operation,
        Func<ExternalApiException, bool> isRetryable,
        CancellationToken cancellationToken)
    {
        var maxRetries = options.Value.MaxAttempts;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(attempt, cancellationToken);
            }
            catch (ExternalApiException exception) when (attempt <= maxRetries && isRetryable(exception))
            {
                var delay = DelayBeforeRetry(attempt, exception.RetryAfter);
                var error = Describe(exception);

                logger.LogWarning(
                    "Error \"{Error}\" occurred during job {JobLabel} at step {StepCode}. Retry attempt {RetryNumber}/{MaxRetries} after {DelaySeconds} seconds.",
                    error, scope.JobLabel, scope.StepCode, attempt, maxRetries, (int)Math.Ceiling(delay.TotalSeconds));

                await journal.EventAsync(new JournalEvent
                {
                    EventCode = EventCodes.RetryScheduled,
                    Level = EventLevel.Warning,
                    TenantId = scope.TenantId,
                    JobId = scope.JobId,
                    WatiJobId = scope.WatiJobId,
                    StepCode = scope.StepCode,
                    Message =
                        $"Job [{scope.JobLabel}] Step [{scope.StepCode}] Attempt [{attempt}] Error [{error}] Retrying after [{(int)Math.Ceiling(delay.TotalSeconds)}] seconds.",
                    Data = new
                    {
                        attempt,
                        retryNumber = attempt,
                        maxRetries,
                        delaySeconds = delay.TotalSeconds,
                        exception.StatusCode,
                        operation = exception.Operation,
                        provider = exception.Provider.ToString(),
                    },
                }, cancellationToken);

                await Task.Delay(delay, time, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 2, 4, 8 ... seconds (InitialDelaySeconds doubled per retry), never more than MaxDelaySeconds.
    /// A server's Retry-After is honoured when it asks for longer, still capped by MaxDelaySeconds.
    /// </summary>
    public TimeSpan DelayBeforeRetry(int retryNumber, TimeSpan? retryAfter = null)
    {
        var settings = options.Value;
        var exponential = settings.InitialDelaySeconds * Math.Pow(2, Math.Max(0, retryNumber - 1));
        var seconds = Math.Min(exponential, settings.MaxDelaySeconds);

        if (retryAfter is { } requested && requested.TotalSeconds > seconds)
        {
            seconds = Math.Min(requested.TotalSeconds, settings.MaxDelaySeconds);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>"HTTP 503 Service Unavailable", or the transport error for a call that got no answer.</summary>
    public static string Describe(ExternalApiException exception) =>
        exception.StatusCode is { } status
            ? $"HTTP {status} {ReasonPhrase(status)}"
            : exception.Message;

    private static string ReasonPhrase(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        402 => "Payment Required",
        403 => "Forbidden",
        404 => "Not Found",
        408 => "Request Timeout",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => string.Empty,
    };
}
