using Microsoft.EntityFrameworkCore;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Wati;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline.Stages;

/// <summary>
/// WATI_PREPARE / WATI_AWAIT: creates the job's WATI job (once) and waits for the WATI dispatcher
/// to finish sending it, then copies the outcome onto the integration job.
///
/// Sending happens in WatiDispatchWorker, not here, so a large broadcast never holds the
/// integration-job lease and manual broadcasts share the same, single sending path.
/// </summary>
public sealed class WatiStage(
    IIntegrationDbContext db,
    WatiJobFactory factory,
    IJobJournal journal,
    TimeProvider time) : IJobStage
{
    private static readonly TimeSpan CheckAgainAfter = TimeSpan.FromSeconds(5);

    public IReadOnlyCollection<IntegrationJobStatus> Handles { get; } = [IntegrationJobStatus.SendingWati];

    public async Task<StageOutcome> ExecuteAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        var watiJob = await db.WatiJobs
            .Where(w => w.IntegrationJobId == job.Id)
            .OrderByDescending(w => w.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (watiJob is null)
        {
            var stepId = await journal.StepStartedAsync(job.Id, StepCodes.WatiPrepare, "Preparing the WATI messages.", cancellationToken);
            job.CurrentStep = StepCodes.WatiPrepare;

            watiJob = await factory.CreateForIntegrationJobAsync(job, cancellationToken);

            await journal.StepFinishedAsync(stepId, StepStatus.Succeeded,
                $"WATI job {watiJob.Reference}: {watiJob.QueuedCount} to send, {watiJob.SkippedCount} skipped.",
                details: new { watiJob.Reference, watiJob.TemplateName, watiJob.BroadcastName, watiJob.QueuedCount, watiJob.SkippedCount },
                cancellationToken: cancellationToken);
        }

        job.WatiEligible = watiJob.TotalRecipients - watiJob.SkippedCount;
        job.WatiSkipped = watiJob.SkippedCount;
        job.WatiSent = watiJob.AcceptedCount;
        job.WatiFailed = watiJob.FailedCount;

        if (!watiJob.IsTerminal)
        {
            job.CurrentStep = StepCodes.WatiAwait;
            job.NextAttemptAtUtc = time.GetUtcNow().Add(CheckAgainAfter);
            job.UpdatedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return StageOutcome.Wait;
        }

        if (watiJob.Status is WatiJobStatus.Failed or WatiJobStatus.CompletedWithErrors && watiJob.ErrorMessage is not null)
        {
            job.ErrorCode = watiJob.ErrorCode;
            job.ErrorMessage = watiJob.ErrorMessage;
        }

        job.Status = IntegrationJobStatus.GeneratingOutput;
        job.CurrentStep = StepCodes.GenerateOutput;
        job.NextAttemptAtUtc = null;
        job.ConsecutiveFailures = 0;
        job.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return StageOutcome.Continue;
    }
}
