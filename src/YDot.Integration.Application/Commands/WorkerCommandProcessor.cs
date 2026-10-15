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

    /// <summary>
    /// The failures a retry can fix: an expired token (once it is replaced), an outage, a batch
    /// whose outcome was never confirmed, missing WATI settings, a refusal of the whole request.
    /// A number WATI refused (WATI_INVALID_NUMBER) or a message WhatsApp itself failed is not one -
    /// sending it again would fail again, or reach somebody twice.
    /// </summary>
    public static readonly IReadOnlyList<string> RetryableWatiFailures =
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
                WorkerCommandType.RetryWatiJob => await RetryWatiJobAsync(command, Read<RetryWatiJobPayload>(command), cancellationToken),
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

    // =============================================================================== retry a check

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
            return (WorkerCommandStatus.Rejected, $"Job {job.JobNumber} is {job.Status}; only a check that failed or was stopped can be retried.", job.Id);
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
    /// Works out where a failed / cancelled check should pick up, and resets only what blocks it:
    /// an uncertain or failed CheckNumber.ai task is replaced by a new one (the old row is kept,
    /// superseded), a timed-out task is polled again (same task_id, no new charge).
    ///
    /// A CHECK'S RETRY NEVER SENDS ANYTHING. Once its numbers are verified it goes on to its output;
    /// messages are sent - and retried - only from a person's selection on the WhatsApp Messages
    /// page. The one exception is an older job whose automatic WATI send is still in flight: it
    /// goes back to wait for that send to finish, so the figures on the job are complete.
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

        var legacySendInFlight = await db.WatiJobs.AnyAsync(w => w.IntegrationJobId == job.Id
                                                                 && (w.Status == WatiJobStatus.Queued || w.Status == WatiJobStatus.Sending), cancellationToken);

        return legacySendInFlight ? IntegrationJobStatus.SendingWati : IntegrationJobStatus.GeneratingOutput;
    }

    // =============================================================================== cancel a check

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

    // =============================================================================== send a selection

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

        if (payload.Selection is not { Count: > 0 } && payload.Recipients is not { Count: > 0 })
        {
            return (WorkerCommandStatus.Rejected, "Choose at least one recipient.", null);
        }

        var watiJob = await watiJobs.CreateManualAsync(command, payload, cancellationToken);

        // A selected number that is not one of this organisation's checked numbers (or is not a
        // phone number at all, or was selected twice) never becomes a message. Say how many, so
        // "I chose 5 and 4 are in the report" is answered where the person is looking.
        var leftOut = watiJob.RequestedCount - watiJob.TotalRecipients;
        var leftOutNote = leftOut > 0
            ? $" Left out of the selection: {LeftOutByReason(watiJob.ExcludedJson, leftOut)}."
            : string.Empty;

        return (WorkerCommandStatus.Completed,
            $"WATI job {watiJob.Reference} created: {watiJob.QueuedCount} to send, {watiJob.SkippedCount} skipped.{leftOutNote}",
            watiJob.Id);
    }

    /// <summary>"1 INVALID_NUMBER, 1 NOT_IN_POOL", from a send's excluded_json (the count alone if it cannot be read).</summary>
    private static string LeftOutByReason(string? excludedJson, int leftOut)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(excludedJson) ? "[]" : excludedJson);
            var counts = document.RootElement.EnumerateArray()
                .Select(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("reason", out var reason) ? reason.GetString() : null)
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .GroupBy(reason => reason!, StringComparer.Ordinal)
                .Select(group => $"{group.Count()} {group.Key}")
                .ToList();
            if (counts.Count > 0)
            {
                return string.Join(", ", counts);
            }
        }
        catch (JsonException)
        {
            // Fall through to the plain count.
        }

        return $"{leftOut} number(s)";
    }

    // =============================================================================== retry a send

    /// <summary>
    /// Puts the failed messages of one finished send back in the queue - only those that failed
    /// for a reason a retry can fix (<see cref="RetryableWatiFailures"/>). Nothing WATI accepted is
    /// sent again and nobody outside the original selection is added: the rows already exist, they
    /// only go from Failed back to Queued.
    ///
    /// ONLY A PERSON'S SEND. An older automatic send messaged a whole file without anybody choosing
    /// the recipients; its failures stay recorded as they are.
    /// </summary>
    private async Task<(WorkerCommandStatus, string, Guid?)> RetryWatiJobAsync(
        WorkerCommand command, RetryWatiJobPayload payload, CancellationToken cancellationToken)
    {
        var pipeline = pipelineOptions.Value;
        if (!pipeline.EnableWati || !pipeline.AllowManualWatiBroadcasts)
        {
            return (WorkerCommandStatus.Rejected, "WATI sending is switched off in the worker configuration (Pipeline:EnableWati / AllowManualWatiBroadcasts).", null);
        }

        var watiJob = await db.WatiJobs.SingleOrDefaultAsync(w => w.Id == payload.WatiJobId && w.TenantId == command.TenantId, cancellationToken);
        if (watiJob is null)
        {
            return (WorkerCommandStatus.Rejected, "That send was not found for this organisation.", null);
        }

        if (watiJob.Source != WatiJobSource.Manual)
        {
            return (WorkerCommandStatus.Rejected,
                $"{watiJob.Reference} was sent automatically after a check, before sends were limited to selected numbers; it cannot be retried. Select the numbers and send them again instead.",
                watiJob.Id);
        }

        if (!JobStatusRules.CanRetryWati(watiJob.Status))
        {
            return (WorkerCommandStatus.Rejected,
                $"{watiJob.Reference} is {watiJob.Status}; only a send that finished with failed messages can be retried.", watiJob.Id);
        }

        var requeued = await RequeueRetryableMessagesAsync(watiJob, cancellationToken);
        if (requeued == 0)
        {
            return (WorkerCommandStatus.Rejected,
                $"None of the failed messages of {watiJob.Reference} can be retried: WATI refused those numbers, or WhatsApp itself failed them.", watiJob.Id);
        }

        var message = $"{requeued} failed message(s) of {watiJob.Reference} are back in the queue (requested by {command.RequestedByName ?? "the web application"}).";
        logger.LogInformation("{Message}", message);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.WatiJobRetried,
            Message = message,
            TenantId = watiJob.TenantId,
            WatiJobId = watiJob.Id,
            StepCode = StepCodes.WatiSendBatch,
            Data = new { command.Reference, requeued },
        }, cancellationToken);

        return (WorkerCommandStatus.Completed, message, watiJob.Id);
    }

    private async Task<int> RequeueRetryableMessagesAsync(WatiJob watiJob, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var retryable = RetryableWatiFailures.ToArray();
        var requeued = await db.WatiMessages
            .Where(m => m.WatiJobId == watiJob.Id && m.Status == WatiMessageStatus.Failed
                        && m.FailedCode != null && retryable.Contains(m.FailedCode))
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
