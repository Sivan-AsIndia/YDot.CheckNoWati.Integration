using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline.Stages;

/// <summary>
/// WATI_AWAIT, FOR OLDER JOBS ONLY.
///
/// A CHECK NO LONGER MESSAGES ANYBODY (15 October 2026). Until then this stage created a WATI job
/// for every finished check and sent the template to all of its verified numbers - 450 messages
/// for a 500-number file, held back only by a test allow-list. Now a check stops at "verified":
/// its numbers wait in the organisation's pool until a person picks the ten or fifty to message
/// on the WhatsApp Messages page (WatiJobFactory.CreateManualAsync).
///
/// What is left here is for a job that was already in this state when the worker was upgraded:
/// if its automatic WATI job exists it is allowed to finish (the dispatcher sends it, under the
/// usual safety rules) and its figures are copied onto the job; if it does not exist yet, none is
/// created and the job goes straight on to its output.
/// </summary>
public sealed class WatiStage(
    IIntegrationDbContext db,
    IJobJournal journal,
    TimeProvider time,
    ILogger<WatiStage> logger) : IJobStage
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
            var message = $"Job {job.JobNumber}: nothing was messaged. A check only verifies numbers; "
                          + $"its {job.WhatsAppYes} WhatsApp number(s) are ready to be picked on the WhatsApp Messages page.";
            logger.LogInformation("{Message}", message);
            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.NumbersReadyForSelection,
                Message = message,
                TenantId = job.TenantId,
                JobId = job.Id,
                StepCode = StepCodes.WatiAwait,
                Data = new { job.WhatsAppYes },
            }, cancellationToken);

            return await ContinueToOutputAsync(job, cancellationToken);
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

        return await ContinueToOutputAsync(job, cancellationToken);
    }

    private async Task<StageOutcome> ContinueToOutputAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        job.Status = IntegrationJobStatus.GeneratingOutput;
        job.CurrentStep = StepCodes.GenerateOutput;
        job.NextAttemptAtUtc = null;
        job.ConsecutiveFailures = 0;
        job.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return StageOutcome.Continue;
    }
}
