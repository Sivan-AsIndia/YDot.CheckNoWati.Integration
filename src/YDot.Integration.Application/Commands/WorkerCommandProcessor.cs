using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Wati;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Commands;

/// <summary>
/// Carries out the requests the web application leaves in int_worker_commands. Each command is
/// claimed by exactly one instance (the lease store sets it to Processing), carried out, and its
/// outcome written back - Completed with a message, Rejected with the business reason, or Failed.
/// </summary>
public sealed class WorkerCommandProcessor(
    IIntegrationDbContext db,
    WatiJobFactory watiJobs,
    WatiTemplateSynchronizer templates,
    IJobJournal journal,
    IOptions<PipelineOptions> pipelineOptions,
    TimeProvider time,
    ILogger<WorkerCommandProcessor> logger)
{
    private const int MaxAttempts = 5;

    private static readonly string[] RetryableWatiFailures =
    [
        ErrorCodes.WatiUnauthorized,
        ErrorCodes.WatiUnavailable,
        ErrorCodes.WatiDeliveryUncertain,
        ErrorCodes.WatiNotConfigured,
        ErrorCodes.WatiRejected,
    ];

    public async Task ProcessAsync(Guid commandId, CancellationToken cancellationToken)
    {
        var command = await db.WorkerCommands.SingleOrDefaultAsync(c => c.Id == commandId, cancellationToken);
        if (command is null || command.Status != WorkerCommandStatus.Processing)
        {
            return;
        }

        try
        {
            var (status, message, entityId) = command.CommandType switch
            {
                WorkerCommandType.RetryJob => await RetryJobAsync(command, Read<RetryJobPayload>(command), cancellationToken),
                WorkerCommandType.CancelJob => await CancelJobAsync(command, Read<CancelJobPayload>(command), cancellationToken),
                WorkerCommandType.SendWatiBroadcast => await SendBroadcastAsync(command, Read<SendWatiBroadcastPayload>(command), cancellationToken),
                WorkerCommandType.SyncWatiTemplates => await SyncTemplatesAsync(cancellationToken),
                _ => (WorkerCommandStatus.Rejected, $"Unknown command type {command.CommandType}.", (Guid?)null),
            };

            Complete(command, status, message, entityId);
        }
        catch (JsonException exception)
        {
            Complete(command, WorkerCommandStatus.Rejected, $"The request could not be read: {exception.Message}", null);
        }
        catch (Exception exception) when (TransientErrors.IsTransient(exception) && command.Attempts < MaxAttempts)
        {
            db.ChangeTracker.Clear();
            command = await db.WorkerCommands.SingleAsync(c => c.Id == commandId, CancellationToken.None);
            command.Status = WorkerCommandStatus.Pending;
            command.LockedBy = null;
            command.LockedUntilUtc = time.GetUtcNow().AddSeconds(15 * command.Attempts);
            command.ResultMessage = Text.Truncate($"Temporary problem, will try again: {exception.Message}", 2000);
            logger.LogWarning("Command {Reference} hit a temporary problem (attempt {Attempt}); it will run again. {Reason}",
                command.Reference, command.Attempts, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            command = await db.WorkerCommands.SingleAsync(c => c.Id == commandId, CancellationToken.None);
            Complete(command, WorkerCommandStatus.Failed, $"The request failed: {exception.Message}", null);
            logger.LogError(exception, "Command {Reference} ({Type}) failed.", command.Reference, command.CommandType);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    // =============================================================================== retry

    private async Task<(WorkerCommandStatus, string, Guid?)> RetryJobAsync(
        WorkerCommand command, RetryJobPayload payload, CancellationToken cancellationToken)
    {
        var job = await db.IntegrationJobs.SingleOrDefaultAsync(j => j.Id == payload.JobId && j.TenantId == command.TenantId, cancellationToken);
        if (job is null)
        {
            return (WorkerCommandStatus.Rejected, "That job was not found for this organisation.", null);
        }

        if (!JobStatusRules.CanRetry(job.Status))
        {
            return (WorkerCommandStatus.Rejected, $"Job {job.JobNumber} is {job.Status}; only a job that failed, was cancelled or completed with errors can be retried.", job.Id);
        }

        var resumeAt = await PrepareResumeAsync(job, cancellationToken);
        var now = time.GetUtcNow();

        job.Status = resumeAt;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.OutcomeMessage = null;
        job.CompletedAtUtc = null;
        job.CancelRequested = false;
        job.ConsecutiveFailures = 0;
        job.NextAttemptAtUtc = now;
        job.ProcessingAttempts++;
        job.UpdatedAtUtc = now;

        var message = $"Job {job.JobNumber} will resume at {resumeAt} (requested by {command.RequestedByName ?? "the web application"}).";
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.JobRetried,
            Message = message,
            TenantId = job.TenantId,
            JobId = job.Id,
            Data = new { command.Reference, resumeAt = resumeAt.ToString() },
        }, cancellationToken);
        logger.LogInformation("{Message}", message);

        return (WorkerCommandStatus.Completed, message, job.Id);
    }

    /// <summary>
    /// Works out where a failed / cancelled job should pick up, and resets only what blocks it:
    /// an uncertain or failed CheckNumber.ai task is replaced by a new one (the old row is kept,
    /// superseded), a timed-out task is polled again (same task_id, no new charge), and WATI
    /// messages that failed for a reason a retry can fix go back to the queue.
    /// </summary>
    private async Task<IntegrationJobStatus> PrepareResumeAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        var parsed = await db.InputFileNumbers.AnyAsync(n => n.JobId == job.Id, cancellationToken);
        if (!parsed)
        {
            return IntegrationJobStatus.Queued;
        }

        var now = time.GetUtcNow();
        var tasks = await db.CheckNumberTasks.Where(t => t.JobId == job.Id && !t.IsSuperseded).ToListAsync(cancellationToken);
        var checkPending = job.RunCheckNumber && job.FreshNumbers > 0
                           && (tasks.Count == 0 || tasks.Any(t => t.State != CheckNumberTaskState.Processed));

        if (checkPending)
        {
            foreach (var task in tasks)
            {
                var uncertainCreate = task.State == CheckNumberTaskState.Creating && task.CreateRequestedAtUtc is not null && task.ProviderTaskId is null;

                if (task.State is CheckNumberTaskState.Failed or CheckNumberTaskState.Uncertain || uncertainCreate)
                {
                    // A person decided to try again: replace the task, keep the old row for audit.
                    task.IsSuperseded = true;
                    task.UpdatedAtUtc = now;
                    var replacement = new CheckNumberTask
                    {
                        TenantId = task.TenantId,
                        JobId = task.JobId,
                        Sequence = task.Sequence,
                        TaskType = task.TaskType,
                        State = CheckNumberTaskState.Creating,
                        SubmittedCount = task.SubmittedCount,
                        CountryCode = task.CountryCode,
                        SubmittedNumbersPath = task.SubmittedNumbersPath,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                    };
                    db.CheckNumberTasks.Add(replacement);
                    await db.SaveChangesAsync(cancellationToken);

                    await db.InputFileNumbers
                        .Where(n => n.CheckNumberTaskId == task.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(n => n.CheckNumberTaskId, replacement.Id), cancellationToken);
                }
                else if (task.State == CheckNumberTaskState.TimedOut)
                {
                    task.State = CheckNumberTaskState.Created;
                    task.PollCount = 0;
                    task.NextPollAtUtc = now;
                    task.ErrorCode = null;
                    task.ErrorMessage = null;
                    task.UpdatedAtUtc = now;
                }
                else if (task.State == CheckNumberTaskState.Downloaded)
                {
                    // Extraction or reading failed: fetch the archive again.
                    task.State = CheckNumberTaskState.Exported;
                    task.UpdatedAtUtc = now;
                }
            }

            return IntegrationJobStatus.CheckingNumbers;
        }

        var verified = !await db.InputFileNumbers.AnyAsync(n => n.JobId == job.Id
                                                                && (n.Classification == NumberClassification.Fresh || n.Classification == NumberClassification.AlreadyChecked)
                                                                && n.FinalWhatsAppStatus == null, cancellationToken);
        if (!verified)
        {
            return IntegrationJobStatus.Verifying;
        }

        if (!job.RunWati)
        {
            return IntegrationJobStatus.GeneratingOutput;
        }

        var watiJob = await db.WatiJobs.Where(w => w.IntegrationJobId == job.Id).OrderByDescending(w => w.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (watiJob is null)
        {
            return IntegrationJobStatus.SendingWati;
        }

        if (watiJob.ErrorCode == ErrorCodes.WatiTemplateMissing)
        {
            // Nothing was ever sent: discard it so the WATI stage builds it again with today's settings.
            await db.WatiMessages.Where(m => m.WatiJobId == watiJob.Id).ExecuteDeleteAsync(cancellationToken);
            db.WatiJobs.Remove(watiJob);
            await db.SaveChangesAsync(cancellationToken);
            return IntegrationJobStatus.SendingWati;
        }

        await RequeueRetryableMessagesAsync(watiJob, cancellationToken);

        // ALWAYS THROUGH THE WATI STAGE, even when nothing was requeued. That stage copies the WATI
        // job's figures (sent, failed, skipped) onto the job, and a finished WATI job simply passes
        // straight through to the output - so a retry never writes an output with stale counts.
        return IntegrationJobStatus.SendingWati;
    }

    private async Task<int> RequeueRetryableMessagesAsync(WatiJob watiJob, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var requeued = await db.WatiMessages
            .Where(m => m.WatiJobId == watiJob.Id && m.Status == WatiMessageStatus.Failed
                        && m.FailedCode != null && RetryableWatiFailures.Contains(m.FailedCode))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, WatiMessageStatus.Queued)
                .SetProperty(m => m.WatiBatchId, (Guid?)null)
                .SetProperty(m => m.RequestStatus, WatiRequestStatus.NotSent)
                .SetProperty(m => m.FailedCode, (string?)null)
                .SetProperty(m => m.FailedReason, (string?)null)
                .SetProperty(m => m.FailedAtUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.ErrorMessage, (string?)null)
                .SetProperty(m => m.CompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken);

        if (requeued > 0)
        {
            await WatiJobCounters.RefreshAsync(db, watiJob, cancellationToken);
            watiJob.Status = WatiJobStatus.Queued;
            watiJob.ErrorCode = null;
            watiJob.ErrorMessage = null;
            watiJob.CompletedAtUtc = null;
            watiJob.NextAttemptAtUtc = now;
            watiJob.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return requeued;
    }

    // =============================================================================== cancel

    private async Task<(WorkerCommandStatus, string, Guid?)> CancelJobAsync(
        WorkerCommand command, CancelJobPayload payload, CancellationToken cancellationToken)
    {
        var job = await db.IntegrationJobs.SingleOrDefaultAsync(j => j.Id == payload.JobId && j.TenantId == command.TenantId, cancellationToken);
        if (job is null)
        {
            return (WorkerCommandStatus.Rejected, "That job was not found for this organisation.", null);
        }

        if (!JobStatusRules.CanCancel(job.Status))
        {
            return (WorkerCommandStatus.Rejected, $"Job {job.JobNumber} has already finished ({job.Status}).", job.Id);
        }

        // The job processor stops it at the next safe point (it claims cancel-requested jobs at
        // once, whatever their schedule), so a task being created or a batch in flight is never
        // cut off half-way.
        job.CancelRequested = true;
        job.NextAttemptAtUtc = time.GetUtcNow();
        job.UpdatedAtUtc = time.GetUtcNow();

        return (WorkerCommandStatus.Completed, $"Job {job.JobNumber} will stop at the next safe point.", job.Id);
    }

    // =============================================================================== broadcast

    private async Task<(WorkerCommandStatus, string, Guid?)> SendBroadcastAsync(
        WorkerCommand command, SendWatiBroadcastPayload payload, CancellationToken cancellationToken)
    {
        var pipeline = pipelineOptions.Value;
        if (!pipeline.EnableWati || !pipeline.AllowManualWatiBroadcasts)
        {
            return (WorkerCommandStatus.Rejected, "WATI sending is switched off in the worker configuration (Pipeline:EnableWati / AllowManualWatiBroadcasts).", null);
        }

        if (string.IsNullOrWhiteSpace(payload.TemplateName))
        {
            return (WorkerCommandStatus.Rejected, "Choose a template.", null);
        }

        if (string.IsNullOrWhiteSpace(payload.BroadcastName))
        {
            return (WorkerCommandStatus.Rejected, "Give the broadcast a name.", null);
        }

        if (payload.Recipients is not { Count: > 0 })
        {
            return (WorkerCommandStatus.Rejected, "Choose at least one recipient.", null);
        }

        var watiJob = await watiJobs.CreateManualAsync(command, payload, cancellationToken);

        // A requested number that is not one of this organisation's WhatsApp-verified numbers (or is
        // not a phone number at all, or was listed twice) never becomes a message. Say how many, so
        // "I chose 5 and 4 are in the report" is answered where the person is looking.
        var leftOut = payload.Recipients.Count - watiJob.TotalRecipients;
        var leftOutNote = leftOut > 0
            ? $" {leftOut} requested number(s) were left out: not WhatsApp-verified numbers of this organisation ({SkipReasons.NotInPool}), not valid, or listed twice."
            : string.Empty;

        return (WorkerCommandStatus.Completed,
            $"WATI job {watiJob.Reference} created: {watiJob.QueuedCount} to send, {watiJob.SkippedCount} skipped.{leftOutNote}",
            watiJob.Id);
    }

    private async Task<(WorkerCommandStatus, string, Guid?)> SyncTemplatesAsync(CancellationToken cancellationToken)
    {
        // The request itself was carried out either way; whether WATI answered is in the message,
        // so the page never claims a refresh that did not happen.
        var result = await templates.SyncAsync(cancellationToken);
        return (WorkerCommandStatus.Completed, result.Describe(), null);
    }

    // =============================================================================== helpers

    private void Complete(WorkerCommand command, WorkerCommandStatus status, string message, Guid? entityId)
    {
        command.Status = status;
        command.ResultMessage = Text.Truncate(message, 2000);
        command.ResultEntityId = entityId;
        command.ProcessedAtUtc = time.GetUtcNow();
        command.LockedBy = null;
        command.LockedUntilUtc = null;

        logger.Log(status == WorkerCommandStatus.Completed ? LogLevel.Information : LogLevel.Warning,
            "Command {Reference} ({Type}) {Status}: {Message}", command.Reference, command.CommandType, status, message);
    }

    private static T Read<T>(WorkerCommand command) =>
        JsonSerializer.Deserialize<T>(command.PayloadJson, JsonDefaults.Options)
        ?? throw new JsonException("The request payload is empty.");
}
