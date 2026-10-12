using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YDot.Integration.Application.Abstractions.Persistence;

namespace YDot.Integration.Application.Pipeline;

/// <summary>
/// Drives one claimed job through as many stages as it can go right now: parse, check, verify,
/// WATI, output - stopping when the job has to wait (CheckNumber.ai still processing, WATI still
/// sending) or reaches a final status.
///
/// Each stage runs in its own DI scope, so a job with 50,000 numbers never accumulates tracked
/// entities across stages, and the lease is renewed between stages so a long job is never taken
/// over by another instance while it is still being worked on. The lease is always released at
/// the end (including on shutdown) so the job can be resumed straight away.
/// </summary>
public sealed class JobProcessor(IServiceScopeFactory scopes, ILeaseStore leases, ILogger<JobProcessor> logger)
{
    /// <summary>Upper bound on stage transitions per claim; a full pipeline needs six.</summary>
    private const int MaxStagesPerClaim = 12;

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            for (var round = 0; round < MaxStagesPerClaim; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                StageOutcome outcome;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var runner = scope.ServiceProvider.GetRequiredService<JobStageRunner>();
                    outcome = await runner.RunNextAsync(jobId, cancellationToken);
                }

                if (outcome != StageOutcome.Continue)
                {
                    return;
                }

                if (!await leases.RenewIntegrationJobAsync(jobId, cancellationToken))
                {
                    logger.LogWarning("Lost the lease on job {JobId}; another instance will continue it.", jobId);
                    return;
                }
            }
        }
        finally
        {
            try
            {
                await leases.ReleaseIntegrationJobAsync(jobId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                // The lease simply expires if it cannot be released now.
                logger.LogWarning(exception, "Could not release the lease on job {JobId}.", jobId);
            }
        }
    }
}
