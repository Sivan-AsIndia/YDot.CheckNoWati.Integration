using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline.Stages;

/// <summary>
/// IDENTIFY_VERIFIED: settles, for every distinct number of the job, the answer the job will act
/// on - the fresh CheckNumber.ai answer, or the saved answer that was reused - and writes it onto
/// the number rows (duplicates included) with a plain-language note for the report.
///
/// THIS IS WHERE A CHECK ENDS. The numbers with WhatsApp = Yes join the organisation's pool on the
/// WhatsApp Messages page, where a person picks the ones to message; nothing is sent from here
/// (until 15 October 2026 the job went on to message every one of them automatically).
/// </summary>
public sealed class VerifyStage(
    IIntegrationDbContext db,
    IJobJournal journal,
    TimeProvider time,
    ILogger<VerifyStage> logger) : IJobStage
{
    private const int PageSize = 5000;

    /// <summary>The note the upload page uses for a reused Yes.</summary>
    public const string SavedYesNote = "Answered from an earlier check — not charged";

    /// <summary>The note the upload page uses for a reused No.</summary>
    public const string SavedNoNote = "Answered from an earlier check — not registered on WhatsApp";

    public IReadOnlyCollection<IntegrationJobStatus> Handles { get; } = [IntegrationJobStatus.Verifying];

    public async Task<StageOutcome> ExecuteAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        job.CurrentStep = StepCodes.IdentifyVerified;
        var stepId = await journal.StepStartedAsync(job.Id, StepCodes.IdentifyVerified, "Identifying the WhatsApp-verified numbers.", cancellationToken);

        // The answers of this job's (current, not superseded) CheckNumber.ai tasks, by phone number.
        var answers = await db.CheckNumberResults
            .Where(r => r.JobId == job.Id
                        && db.CheckNumberTasks.Any(t => t.Id == r.CheckNumberTaskId && !t.IsSuperseded))
            .Select(r => new { r.Id, r.PhoneNumberId, r.WhatsAppStatus, r.Note })
            .ToListAsync(cancellationToken);
        var answerByPhone = answers
            .GroupBy(a => a.PhoneNumberId)
            .ToDictionary(g => g.Key, g => g.Last());

        int yes = 0, no = 0, unknown = 0;
        var finalByRow = new Dictionary<Guid, (WhatsAppStatus Status, Guid? ResultId, string? Note)>();

        // ---- First occurrences: the job's distinct numbers --------------------------------------
        var skip = 0;
        while (true)
        {
            var page = await db.InputFileNumbers
                .Where(n => n.JobId == job.Id
                            && (n.Classification == NumberClassification.Fresh || n.Classification == NumberClassification.AlreadyChecked))
                .OrderBy(n => n.Sequence)
                .Skip(skip)
                .Take(PageSize)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var row in page)
            {
                (WhatsAppStatus status, Guid? resultId, string? note) decision = row.Classification switch
                {
                    NumberClassification.AlreadyChecked => (
                        row.ReusedWhatsAppStatus ?? WhatsAppStatus.Unknown,
                        row.ReusedFromResultId,
                        row.ReusedWhatsAppStatus == WhatsAppStatus.Yes ? SavedYesNote : SavedNoNote),

                    _ when row.PhoneNumberId is { } phoneId && answerByPhone.TryGetValue(phoneId, out var answer) => (
                        answer.WhatsAppStatus,
                        answer.Id,
                        answer.Note ?? (answer.WhatsAppStatus == WhatsAppStatus.Yes
                            ? "Registered on WhatsApp"
                            : answer.WhatsAppStatus == WhatsAppStatus.No ? "Not registered on WhatsApp" : "Could not be verified")),

                    _ when !job.RunCheckNumber => (
                        WhatsAppStatus.Unknown, null, "Not checked — CheckNumber.ai is switched off for this job"),

                    _ => (WhatsAppStatus.Unknown, null, "No answer from CheckNumber.ai"),
                };

                row.FinalWhatsAppStatus = decision.status;
                row.CheckResultId = decision.resultId;
                row.ResultNote = decision.note;
                finalByRow[row.Id] = (decision.status, decision.resultId, decision.note);

                switch (decision.status)
                {
                    case WhatsAppStatus.Yes: yes++; break;
                    case WhatsAppStatus.No: no++; break;
                    default: unknown++; break;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            DetachNumbers();
            skip += page.Count;
        }

        // ---- Duplicates carry their first occurrence's answer --------------------------------------
        skip = 0;
        while (true)
        {
            var page = await db.InputFileNumbers
                .Where(n => n.JobId == job.Id && n.Classification == NumberClassification.Duplicate)
                .OrderBy(n => n.Sequence)
                .Skip(skip)
                .Take(PageSize)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var row in page)
            {
                if (row.DuplicateOfId is { } firstId && finalByRow.TryGetValue(firstId, out var first))
                {
                    row.FinalWhatsAppStatus = first.Status;
                    row.CheckResultId = first.ResultId;
                    row.ResultNote = "Repeated in this file — checked once";
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            DetachNumbers();
            skip += page.Count;
        }

        job.WhatsAppYes = yes;
        job.WhatsAppNo = no;
        job.WhatsAppUnknown = unknown;

        // ALWAYS ON TO THE OUTPUT, whatever RunWati says. An older job may still carry RunWati =
        // true from before automatic sending was retired; it is not messaged either.
        job.Status = IntegrationJobStatus.GeneratingOutput;
        job.CurrentStep = StepCodes.GenerateOutput;
        job.ConsecutiveFailures = 0;
        job.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var message = $"{yes} number(s) on WhatsApp, {no} not on WhatsApp, {unknown} unverified.";
        logger.LogInformation("Job {JobNumber}: {Message}", job.JobNumber, message);
        await journal.StepFinishedAsync(stepId, StepStatus.Succeeded, message, details: new { yes, no, unknown }, cancellationToken: cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.VerifiedNumbersIdentified,
            Message = message,
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = StepCodes.IdentifyVerified,
            Data = new { yes, no, unknown },
        }, cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.NumbersReadyForSelection,
            Message = yes > 0
                ? $"{yes} WhatsApp number(s) are ready to be picked on the WhatsApp Messages page. Nothing was messaged."
                : "No number of this file is on WhatsApp, so there is nothing to pick for a message.",
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = StepCodes.IdentifyVerified,
            Data = new { yes },
        }, cancellationToken);

        return StageOutcome.Continue;
    }

    private void DetachNumbers()
    {
        foreach (var entry in db.ChangeTracker.Entries<InputFileNumber>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
