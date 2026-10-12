using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.CheckNumber;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Retry;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline.Stages;

/// <summary>
/// Everything that talks to CheckNumber.ai, as a small state machine per task:
///
///   Creating -> Created (task_id stored) -> Exported (result_url) -> Downloaded -> Extracted -> Processed
///
/// Each arrow is committed before the next one starts, so after a restart the stage simply
/// continues each task from the state it is in:
///
///   - task created but not finished   -> keep polling the same task_id (no new task, no new charge)
///   - exported but not downloaded     -> download now (a fresh result_url is fetched if the old
///                                        pre-signed link expired)
///   - downloaded but not read         -> extract and read the archive already on disk
///
/// POLLING NEVER BLOCKS. Between checks the job is parked (NextAttemptAtUtc) and the worker
/// serves other jobs; CheckNumber:PollingIntervalSeconds and MaxPollingAttempts bound it.
/// </summary>
public sealed class CheckNumberStage(
    IIntegrationDbContext db,
    ICheckNumberClient client,
    IArchiveExtractor extractor,
    IPhoneNumberRegistry registry,
    IDataPaths paths,
    RetryExecutor retry,
    IJobJournal journal,
    IOptions<CheckNumberOptions> options,
    TimeProvider time,
    ILogger<CheckNumberStage> logger) : IJobStage
{
    public IReadOnlyCollection<IntegrationJobStatus> Handles { get; } = [IntegrationJobStatus.CheckingNumbers];

    private CheckNumberOptions Settings => options.Value;

    public async Task<StageOutcome> ExecuteAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        var tasks = await db.CheckNumberTasks
            .Where(t => t.JobId == job.Id && !t.IsSuperseded)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            tasks = await PlanTasksAsync(job, cancellationToken);
        }

        DateTimeOffset? earliestWait = null;

        foreach (var task in tasks)
        {
            if (await AdvanceAsync(job, task, tasks, cancellationToken) is { } waitUntil)
            {
                earliestWait = Min(earliestWait, waitUntil);
            }
        }

        if (tasks.All(t => t.State == CheckNumberTaskState.Processed))
        {
            job.Status = IntegrationJobStatus.Verifying;
            job.CurrentStep = StepCodes.IdentifyVerified;
            job.NextAttemptAtUtc = null;
            job.ConsecutiveFailures = 0;
            job.UpdatedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return StageOutcome.Continue;
        }

        job.CurrentStep = StepCodes.CheckNumberPoll;
        job.NextAttemptAtUtc = earliestWait ?? time.GetUtcNow().AddSeconds(Settings.PollingIntervalSeconds);
        job.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return StageOutcome.Wait;
    }

    /// <summary>
    /// Drives one task as far as it can go right now. Returns the time it must wait until (a task
    /// still processing on CheckNumber.ai's side), or null once it is fully processed.
    /// </summary>
    private async Task<DateTimeOffset?> AdvanceAsync(
        IntegrationJob job, CheckNumberTask task, List<CheckNumberTask> allTasks, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (task.State)
            {
                case CheckNumberTaskState.Creating:
                    await CreateTaskAsync(job, task, cancellationToken);
                    break;

                case CheckNumberTaskState.Created:
                    if (task.NextPollAtUtc is { } due && due > time.GetUtcNow())
                    {
                        return due;
                    }

                    await PollTaskAsync(job, task, allTasks, cancellationToken);

                    if (task.State == CheckNumberTaskState.Created)
                    {
                        return task.NextPollAtUtc ?? time.GetUtcNow();
                    }

                    break;

                case CheckNumberTaskState.Exported:
                    await DownloadAsync(job, task, cancellationToken);
                    break;

                case CheckNumberTaskState.Downloaded:
                    await ExtractAsync(job, task, cancellationToken);
                    break;

                case CheckNumberTaskState.Extracted:
                    await ProcessResultsAsync(job, task, allTasks, cancellationToken);
                    break;

                case CheckNumberTaskState.Processed:
                    return null;

                default:
                    // Failed / Uncertain / TimedOut are only left behind by a run that already failed
                    // the job; the retry command resets them before the job is picked up again.
                    throw new JobStageException(task.ErrorCode ?? ErrorCodes.CheckNumberTaskFailed,
                        task.ErrorMessage ?? $"CheckNumber.ai task {task.Sequence} is {task.State}.");
            }
        }
    }

    // ============================================================================ planning

    /// <summary>
    /// Splits the fresh numbers into tasks (by MaxNumbersPerTask, and by country when
    /// SplitTasksByCountry is on), writes each task row and links the numbers to it - all in one
    /// transaction, before anything is sent.
    /// </summary>
    private async Task<List<CheckNumberTask>> PlanTasksAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        var fresh = await db.InputFileNumbers
            .Where(n => n.JobId == job.Id && n.Classification == NumberClassification.Fresh)
            .OrderBy(n => n.Sequence)
            .Select(n => new { n.Id, Number = n.NormalizedNumber!, n.CountryCode })
            .ToListAsync(cancellationToken);

        if (fresh.Count == 0)
        {
            return [];
        }

        var groups = Settings.SplitTasksByCountry
            ? fresh.GroupBy(n => n.CountryCode ?? string.Empty).Select(g => (Country: (string?)g.Key, Numbers: g.ToList()))
            : [(Country: (string?)null, Numbers: fresh)];

        var now = time.GetUtcNow();
        var tasks = new List<CheckNumberTask>();
        var sequence = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var (country, numbers) in groups)
        {
            foreach (var chunk in numbers.Chunk(Settings.MaxNumbersPerTask))
            {
                sequence++;
                var task = new CheckNumberTask
                {
                    TenantId = job.TenantId,
                    JobId = job.Id,
                    Sequence = sequence,
                    TaskType = Settings.TaskType,
                    State = CheckNumberTaskState.Creating,
                    SubmittedCount = chunk.Length,
                    CountryCode = string.IsNullOrEmpty(country) ? null : country,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };

                // The exact upload, kept for audit and for anybody raising a ticket with CheckNumber.ai.
                var folder = paths.CheckNumberFolder(job.JobNumber, sequence);
                Directory.CreateDirectory(folder);
                var submittedPath = Path.Combine(folder, "submitted-numbers.txt");
                // Written without a byte-order mark, so it is byte for byte what the API client sends.
                await File.WriteAllTextAsync(submittedPath, string.Join('\n', chunk.Select(n => n.Number)),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                task.SubmittedNumbersPath = paths.ToRelative(submittedPath);

                db.CheckNumberTasks.Add(task);
                await db.SaveChangesAsync(cancellationToken);

                var ids = chunk.Select(n => n.Id).ToList();
                await db.InputFileNumbers
                    .Where(n => ids.Contains(n.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.CheckNumberTaskId, task.Id), cancellationToken);

                tasks.Add(task);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Job {JobNumber}: {Fresh} number(s) planned in {Tasks} CheckNumber.ai task(s).",
            job.JobNumber, fresh.Count, tasks.Count);

        return tasks;
    }

    // ============================================================================ create

    private async Task CreateTaskAsync(IntegrationJob job, CheckNumberTask task, CancellationToken cancellationToken)
    {
        // A create request was already sent once and no task_id came back - the worker stopped or
        // crashed while it was in flight. CheckNumber.ai may have created (and billed) the task, and
        // it offers no way to look a task up except by its id, so a person must decide.
        if (task.CreateRequestedAtUtc is not null && task.ProviderTaskId is null)
        {
            await MarkUncertainAsync(job, task,
                $"Creating CheckNumber.ai task {task.Sequence} was interrupted at {task.CreateRequestedAtUtc:u} before an answer arrived. "
                + "The task may exist and be billed. Check the CheckNumber.ai dashboard, then use Retry to create it again.",
                cancellationToken);
        }

        var stepId = await journal.StepStartedAsync(job.Id, StepCodes.CheckNumberCreateTask,
            $"Creating CheckNumber.ai task {task.Sequence} for {task.SubmittedCount} number(s).", cancellationToken);

        var numbers = await db.InputFileNumbers
            .Where(n => n.CheckNumberTaskId == task.Id && n.Classification == NumberClassification.Fresh)
            .OrderBy(n => n.Sequence)
            .Select(n => n.NormalizedNumber!)
            .ToListAsync(cancellationToken);

        // Committed BEFORE the call: from this moment the task may exist on CheckNumber.ai's side.
        task.CreateRequestedAtUtc = time.GetUtcNow();
        task.UpdatedAtUtc = task.CreateRequestedAtUtc.Value;
        job.CurrentStep = StepCodes.CheckNumberCreateTask;
        await db.SaveChangesAsync(cancellationToken);

        var scope = new RetryScope(job.JobNumber, StepCodes.CheckNumberCreateTask, job.TenantId, job.Id);
        var context = new ApiCallContext
        {
            TenantId = job.TenantId,
            JobId = job.Id,
            RelatedEntityId = task.Id,
            JobLabel = job.JobNumber,
            StepCode = StepCodes.CheckNumberCreateTask,
        };

        CheckNumberCreatedTask created;
        try
        {
            created = await retry.ExecuteAsync(
                scope,
                (attempt, token) => client.CreateTaskAsync($"{job.JobNumber}-task{task.Sequence}.txt", numbers, context, attempt, token),
                exception => exception.IsSafeToRetryWithSideEffects,
                cancellationToken);
        }
        catch (ExternalApiException exception) when (exception.IsSafeToRetryWithSideEffects)
        {
            // Never reached CheckNumber.ai, or it explicitly refused without processing (429/503):
            // nothing was created, so it is safe to try again later.
            task.CreateRequestedAtUtc = null;
            task.ErrorMessage = Text.Truncate(exception.Message, 2000);
            task.UpdatedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "CheckNumber.ai unavailable; will try again.",
                ErrorCodes.CheckNumberUnavailable, exception.Message, cancellationToken: CancellationToken.None);
            throw new JobStageException(ErrorCodes.CheckNumberUnavailable,
                $"CheckNumber.ai could not be reached to create the task: {exception.Message}", isTransient: true, exception);
        }
        catch (ExternalApiException exception) when (exception.OutcomeUncertain)
        {
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "Outcome of the create request is uncertain.",
                ErrorCodes.CheckNumberCreateUncertain, exception.Message, cancellationToken: CancellationToken.None);
            await MarkUncertainAsync(job, task,
                $"CheckNumber.ai did not confirm task {task.Sequence} ({exception.Message}). The task may exist and be billed, "
                + "so it was NOT created again automatically. Check the CheckNumber.ai dashboard, then use Retry to create it again.",
                cancellationToken);
            throw; // unreachable: MarkUncertainAsync throws
        }
        catch (ExternalApiException exception)
        {
            task.State = CheckNumberTaskState.Failed;
            task.ErrorCode = ErrorCodes.CheckNumberRejected;
            task.ErrorMessage = Text.Truncate(exception.Message, 2000);
            task.ProviderMessage = Text.Truncate(exception.ResponseBody, 2000);
            task.UpdatedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "CheckNumber.ai refused the task.",
                ErrorCodes.CheckNumberRejected, exception.Message, cancellationToken: CancellationToken.None);
            throw new JobStageException(ErrorCodes.CheckNumberRejected, $"CheckNumber.ai refused the task: {exception.Message}", inner: exception);
        }

        // The task is billed from here on: persist its id even if shutdown was requested meanwhile.
        var now = time.GetUtcNow();
        task.ProviderTaskId = created.TaskId;
        task.State = CheckNumberTaskState.Created;
        task.ProviderStatus = created.Status;
        task.Total = created.Total;
        task.EstimatedAmount = created.EstimatedAmount;
        task.Currency = created.Currency;
        task.ProviderMessage = Text.Truncate(created.Message, 2000);
        task.ProviderCreatedAtUtc = created.CreatedAt;
        task.ErrorCode = null;
        task.ErrorMessage = null;
        task.NextPollAtUtc = now.AddSeconds(Settings.InitialPollDelaySeconds);
        task.UpdatedAtUtc = now;

        job.EstimatedCost = (job.EstimatedCost ?? 0) + (created.EstimatedAmount ?? 0);
        job.CostCurrency ??= created.Currency;
        job.ConsecutiveFailures = 0;
        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(CancellationToken.None);

        var message = $"Created CheckNumber.ai task {created.TaskId} for {created.Total} number(s), status {created.Status}, "
                      + $"estimated cost {created.EstimatedAmount} {created.Currency}.";
        logger.LogInformation("Job {JobNumber}: {Message} CheckNumber.ai: {ProviderMessage}", job.JobNumber, message, created.Message);

        if (created.Total != numbers.Count)
        {
            logger.LogWarning("Job {JobNumber}: task {TaskId} accepted {Total} numbers but {Submitted} were submitted.",
                job.JobNumber, created.TaskId, created.Total, numbers.Count);
        }

        await journal.StepFinishedAsync(stepId, StepStatus.Succeeded, message,
            details: new { taskId = created.TaskId, created.Total, submitted = numbers.Count, created.EstimatedAmount, created.Currency },
            cancellationToken: CancellationToken.None);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.TaskCreated,
            Message = message,
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = StepCodes.CheckNumberCreateTask,
            Data = new { taskId = created.TaskId, sequence = task.Sequence },
        }, CancellationToken.None);
    }

    private async Task MarkUncertainAsync(IntegrationJob job, CheckNumberTask task, string message, CancellationToken cancellationToken)
    {
        task.State = CheckNumberTaskState.Uncertain;
        task.ErrorCode = ErrorCodes.CheckNumberCreateUncertain;
        task.ErrorMessage = Text.Truncate(message, 2000);
        task.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogError("Job {JobNumber}: {Message}", job.JobNumber, message);
        throw new JobStageException(ErrorCodes.CheckNumberCreateUncertain, message);
    }

    // ============================================================================ poll

    private async Task PollTaskAsync(IntegrationJob job, CheckNumberTask task, List<CheckNumberTask> allTasks, CancellationToken cancellationToken)
    {
        var pollNumber = task.PollCount + 1;
        var started = time.GetUtcNow();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var context = new ApiCallContext
        {
            TenantId = job.TenantId,
            JobId = job.Id,
            RelatedEntityId = task.Id,
            JobLabel = job.JobNumber,
            StepCode = StepCodes.CheckNumberPoll,
        };

        job.CurrentStep = StepCodes.CheckNumberPoll;
        CheckNumberTaskStatus? status = null;
        ExternalApiException? failure = null;

        try
        {
            status = await retry.ExecuteAsync(
                new RetryScope(job.JobNumber, StepCodes.CheckNumberPoll, job.TenantId, job.Id),
                (attempt, token) => client.GetTaskAsync(task.ProviderTaskId!, context, attempt, token),
                exception => exception.IsTransient,
                cancellationToken);
        }
        catch (ExternalApiException exception) when (exception.IsTransient)
        {
            failure = exception;
        }
        catch (ExternalApiException exception)
        {
            task.State = CheckNumberTaskState.Failed;
            task.ErrorCode = ErrorCodes.CheckNumberTaskFailed;
            task.ErrorMessage = Text.Truncate($"Status check refused: {exception.Message}", 2000);
            task.UpdatedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            throw new JobStageException(ErrorCodes.CheckNumberTaskFailed,
                $"CheckNumber.ai refused the status check for task {task.ProviderTaskId}: {exception.Message}", inner: exception);
        }

        stopwatch.Stop();
        var now = time.GetUtcNow();

        db.CheckNumberTaskPolls.Add(new CheckNumberTaskPoll
        {
            CheckNumberTaskId = task.Id,
            JobId = job.Id,
            PollNumber = pollNumber,
            PolledAtUtc = started,
            HttpStatusCode = status?.HttpStatusCode ?? failure?.StatusCode,
            ProviderStatus = status?.Status,
            Total = status?.Total,
            Success = status?.Success,
            Failure = status?.Failure,
            CachedCount = status?.CachedCount,
            HasResultUrl = !string.IsNullOrWhiteSpace(status?.ResultUrl),
            DurationMs = stopwatch.ElapsedMilliseconds,
            ErrorMessage = Text.Truncate(failure?.Message, 2000),
        });

        task.PollCount = pollNumber;
        task.LastPolledAtUtc = started;
        task.UpdatedAtUtc = now;

        if (status is not null)
        {
            var previousStatus = task.ProviderStatus;
            task.ProviderStatus = status.Status;
            task.Total = status.Total;
            task.Success = status.Success;
            task.Failure = status.Failure;
            task.CachedCount = status.CachedCount;
            task.ActualAmount = status.ActualAmount ?? task.ActualAmount;
            task.Currency = status.Currency ?? task.Currency;
            task.ProviderUpdatedAtUtc = status.UpdatedAt;

            job.CheckedNumbers = allTasks.Sum(t => Math.Min(t.SubmittedCount, (t.Success ?? 0) + (t.Failure ?? 0)));

            if (CheckNumberStatuses.IsExported(status.Status) && !string.IsNullOrWhiteSpace(status.ResultUrl))
            {
                task.State = CheckNumberTaskState.Exported;
                task.ResultUrl = status.ResultUrl;
                task.ExportedAtUtc = now;
                task.NextPollAtUtc = null;
                job.ConsecutiveFailures = 0;
                await db.SaveChangesAsync(cancellationToken);

                var message = $"CheckNumber.ai task {task.ProviderTaskId} is exported: {status.Success} checked, {status.Failure} failed, "
                              + $"{status.CachedCount ?? 0} from cache, cost {status.ActualAmount} {status.Currency}.";
                logger.LogInformation("Job {JobNumber}: {Message}", job.JobNumber, message);
                await journal.EventAsync(new JournalEvent
                {
                    EventCode = EventCodes.TaskExported,
                    Message = message,
                    TenantId = job.TenantId,
                    JobId = job.Id,
                    StepCode = StepCodes.CheckNumberPoll,
                    Data = new { taskId = task.ProviderTaskId, polls = pollNumber, status.Total, status.Success, status.Failure, status.CachedCount },
                }, cancellationToken);
                return;
            }

            if (CheckNumberStatuses.IsFailed(status.Status))
            {
                task.State = CheckNumberTaskState.Failed;
                task.ErrorCode = ErrorCodes.CheckNumberTaskFailed;
                task.ErrorMessage = $"CheckNumber.ai task {task.ProviderTaskId} ended with status '{status.Status}'.";
                await db.SaveChangesAsync(CancellationToken.None);
                throw new JobStageException(ErrorCodes.CheckNumberTaskFailed, task.ErrorMessage);
            }

            if (!string.Equals(previousStatus, status.Status, StringComparison.OrdinalIgnoreCase))
            {
                await journal.EventAsync(new JournalEvent
                {
                    EventCode = EventCodes.TaskPolled,
                    Message = $"CheckNumber.ai task {task.ProviderTaskId} is {status.Status}: {status.Success + status.Failure}/{status.Total} done.",
                    TenantId = job.TenantId,
                    JobId = job.Id,
                    StepCode = StepCodes.CheckNumberPoll,
                }, cancellationToken);
            }

            logger.LogInformation(
                "Job {JobNumber}: task {TaskId} is {Status} ({Done}/{Total}), poll {Poll}/{MaxPolls}.",
                job.JobNumber, task.ProviderTaskId, status.Status, status.Success + status.Failure, status.Total,
                pollNumber, Settings.MaxPollingAttempts);
        }
        else
        {
            logger.LogWarning("Job {JobNumber}: status check {Poll}/{MaxPolls} for task {TaskId} failed: {Reason}",
                job.JobNumber, pollNumber, Settings.MaxPollingAttempts, task.ProviderTaskId, failure!.Message);
        }

        if (pollNumber >= Settings.MaxPollingAttempts)
        {
            task.State = CheckNumberTaskState.TimedOut;
            task.ErrorCode = ErrorCodes.CheckNumberPollTimeout;
            task.ErrorMessage =
                $"CheckNumber.ai task {task.ProviderTaskId} was not exported after {pollNumber} status checks. It may still finish: "
                + "use Retry to resume checking the same task (no new task, no new charge).";
            task.NextPollAtUtc = null;
            await db.SaveChangesAsync(CancellationToken.None);
            throw new JobStageException(ErrorCodes.CheckNumberPollTimeout, task.ErrorMessage);
        }

        task.NextPollAtUtc = now.Add(PollingDelay(pollNumber));
        await db.SaveChangesAsync(cancellationToken);
    }

    private TimeSpan PollingDelay(int pollsDone)
    {
        var seconds = Settings.PollingIntervalSeconds * Math.Pow(Settings.PollingBackoffMultiplier, Math.Max(0, pollsDone - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, Settings.MaxPollingIntervalSeconds));
    }

    // ============================================================================ download

    private async Task DownloadAsync(IntegrationJob job, CheckNumberTask task, CancellationToken cancellationToken)
    {
        var resultFile = await db.CheckNumberResultFiles.SingleOrDefaultAsync(r => r.CheckNumberTaskId == task.Id, cancellationToken);
        var now = time.GetUtcNow();

        if (resultFile is null)
        {
            resultFile = new CheckNumberResultFile
            {
                CheckNumberTaskId = task.Id,
                JobId = job.Id,
                ProviderTaskId = task.ProviderTaskId!,
                CreatedAtUtc = now,
            };
            db.CheckNumberResultFiles.Add(resultFile);
        }

        var folder = paths.CheckNumberFolder(job.JobNumber, task.Sequence);
        Directory.CreateDirectory(folder);
        var zipPath = Path.Combine(folder, "result.zip");

        resultFile.ResultUrl = task.ResultUrl;
        resultFile.DownloadStatus = TransferStatus.InProgress;
        resultFile.DownloadAttempts++;
        resultFile.DownloadStartedAtUtc = now;
        resultFile.DownloadError = null;
        resultFile.UpdatedAtUtc = now;
        job.CurrentStep = StepCodes.CheckNumberDownload;
        await db.SaveChangesAsync(cancellationToken);

        var stepId = await journal.StepStartedAsync(job.Id, StepCodes.CheckNumberDownload,
            $"Downloading the result of task {task.ProviderTaskId} from {Text.RedactUrl(task.ResultUrl)}.", cancellationToken);

        var context = new ApiCallContext
        {
            TenantId = job.TenantId,
            JobId = job.Id,
            RelatedEntityId = task.Id,
            JobLabel = job.JobNumber,
            StepCode = StepCodes.CheckNumberDownload,
        };
        var scope = new RetryScope(job.JobNumber, StepCodes.CheckNumberDownload, job.TenantId, job.Id);

        CheckNumberDownload download;
        try
        {
            try
            {
                download = await retry.ExecuteAsync(scope,
                    (attempt, token) => client.DownloadResultAsync(task.ResultUrl!, zipPath, context, attempt, token),
                    exception => exception.IsTransient, cancellationToken);
            }
            catch (ExternalApiException exception) when (exception.StatusCode == 403)
            {
                // Pre-signed links expire (X-Amz-Expires); gettasks issues a fresh one.
                logger.LogWarning("Job {JobNumber}: the result link of task {TaskId} was refused (expired?); requesting a fresh link.",
                    job.JobNumber, task.ProviderTaskId);

                var refreshed = await retry.ExecuteAsync(scope,
                    (attempt, token) => client.GetTaskAsync(task.ProviderTaskId!, context with { StepCode = StepCodes.CheckNumberPoll }, attempt, token),
                    e => e.IsTransient, cancellationToken);

                if (string.IsNullOrWhiteSpace(refreshed.ResultUrl))
                {
                    throw;
                }

                task.ResultUrl = refreshed.ResultUrl;
                resultFile.ResultUrl = refreshed.ResultUrl;
                await db.SaveChangesAsync(cancellationToken);

                download = await retry.ExecuteAsync(scope,
                    (attempt, token) => client.DownloadResultAsync(refreshed.ResultUrl, zipPath, context, attempt, token),
                    e => e.IsTransient, cancellationToken);
            }
        }
        catch (ExternalApiException exception)
        {
            resultFile.DownloadStatus = TransferStatus.Failed;
            resultFile.HttpStatusCode = exception.StatusCode;
            resultFile.DownloadError = Text.Truncate(exception.Message, 2000);
            resultFile.DownloadCompletedAtUtc = time.GetUtcNow();
            resultFile.UpdatedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "Result download failed.", ErrorCodes.CheckNumberDownloadFailed,
                exception.Message, cancellationToken: CancellationToken.None);
            throw new JobStageException(ErrorCodes.CheckNumberDownloadFailed,
                $"The result archive of task {task.ProviderTaskId} could not be downloaded: {exception.Message}",
                isTransient: exception.IsTransient, exception);
        }

        var done = time.GetUtcNow();
        resultFile.DownloadStatus = TransferStatus.Succeeded;
        resultFile.HttpStatusCode = download.HttpStatusCode;
        resultFile.FileSizeBytes = download.SizeBytes;
        resultFile.FileSha256 = download.Sha256;
        resultFile.LocalZipPath = paths.ToRelative(zipPath);
        resultFile.DownloadCompletedAtUtc = done;
        resultFile.DownloadDurationMs = download.DurationMs;
        resultFile.UpdatedAtUtc = done;
        task.State = CheckNumberTaskState.Downloaded;
        task.UpdatedAtUtc = done;
        await db.SaveChangesAsync(cancellationToken);

        var message = $"Downloaded the result of task {task.ProviderTaskId}: {download.SizeBytes:N0} bytes in {download.DurationMs} ms "
                      + $"(HTTP {download.HttpStatusCode}) to {resultFile.LocalZipPath}.";
        logger.LogInformation("Job {JobNumber}: {Message}", job.JobNumber, message);
        await journal.StepFinishedAsync(stepId, StepStatus.Succeeded, message,
            details: new { download.HttpStatusCode, download.SizeBytes, download.DurationMs, download.Sha256, path = resultFile.LocalZipPath },
            cancellationToken: cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.ResultDownloaded,
            Message = message,
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = StepCodes.CheckNumberDownload,
        }, cancellationToken);
    }

    // ============================================================================ extract

    private async Task ExtractAsync(IntegrationJob job, CheckNumberTask task, CancellationToken cancellationToken)
    {
        var resultFile = await db.CheckNumberResultFiles.SingleAsync(r => r.CheckNumberTaskId == task.Id, cancellationToken);
        var zipPath = paths.ToAbsolute(resultFile.LocalZipPath!);
        var extractedFolder = Path.Combine(Path.GetDirectoryName(zipPath)!, "extracted");

        if (!File.Exists(zipPath))
        {
            // The archive vanished (volume cleaned?): go back one state and download it again.
            task.State = CheckNumberTaskState.Exported;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        resultFile.ExtractionStatus = TransferStatus.InProgress;
        resultFile.ExtractionError = null;
        resultFile.UpdatedAtUtc = time.GetUtcNow();
        job.CurrentStep = StepCodes.CheckNumberExtract;
        await db.SaveChangesAsync(cancellationToken);

        var stepId = await journal.StepStartedAsync(job.Id, StepCodes.CheckNumberExtract, $"Extracting {resultFile.LocalZipPath}.", cancellationToken);

        IReadOnlyList<ArchiveEntryInfo> entries;
        try
        {
            if (Directory.Exists(extractedFolder))
            {
                Directory.Delete(extractedFolder, recursive: true);
            }

            entries = await extractor.ExtractAsync(zipPath, extractedFolder, Settings.MaxExtractedBytes, Settings.MaxArchiveEntries, cancellationToken);
        }
        catch (ArchiveExtractionException exception)
        {
            resultFile.ExtractionStatus = TransferStatus.Failed;
            resultFile.ExtractionError = Text.Truncate(exception.Message, 2000);
            resultFile.UpdatedAtUtc = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "Extraction failed.", ErrorCodes.CheckNumberExtractFailed,
                exception.Message, cancellationToken: CancellationToken.None);
            throw new JobStageException(ErrorCodes.CheckNumberExtractFailed,
                $"The result archive of task {task.ProviderTaskId} could not be extracted: {exception.Message}", inner: exception);
        }

        var now = time.GetUtcNow();
        resultFile.ExtractionStatus = TransferStatus.Succeeded;
        resultFile.ExtractedPath = paths.ToRelative(extractedFolder);
        resultFile.ExtractedFileCount = entries.Count;
        resultFile.ExtractedEntriesJson = JsonDefaults.Serialize(entries);
        resultFile.ExtractedAtUtc = now;
        resultFile.UpdatedAtUtc = now;
        task.State = CheckNumberTaskState.Extracted;
        task.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var message = $"Extracted {entries.Count} file(s) from the result of task {task.ProviderTaskId}: {string.Join(", ", entries.Select(e => e.Name))}.";
        await journal.StepFinishedAsync(stepId, StepStatus.Succeeded, message, details: entries, cancellationToken: cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.ResultExtracted,
            Message = message,
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = StepCodes.CheckNumberExtract,
        }, cancellationToken);
    }

    // ============================================================================ results

    private async Task ProcessResultsAsync(IntegrationJob job, CheckNumberTask task, List<CheckNumberTask> allTasks, CancellationToken cancellationToken)
    {
        var resultFile = await db.CheckNumberResultFiles.SingleAsync(r => r.CheckNumberTaskId == task.Id, cancellationToken);
        var extractedFolder = paths.ToAbsolute(resultFile.ExtractedPath!);
        job.CurrentStep = StepCodes.CheckNumberProcessResults;

        var stepId = await journal.StepStartedAsync(job.Id, StepCodes.CheckNumberProcessResults,
            $"Reading the answers of task {task.ProviderTaskId}.", cancellationToken);

        ResultRows rows;
        try
        {
            rows = CheckNumberResultReader.Read(extractedFolder);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "The result files could not be read.", ErrorCodes.CheckNumberResultInvalid,
                exception.Message, cancellationToken: CancellationToken.None);
            throw new JobStageException(ErrorCodes.CheckNumberResultInvalid, exception.Message, inner: exception);
        }

        var submitted = await db.InputFileNumbers
            .Where(n => n.CheckNumberTaskId == task.Id && n.Classification == NumberClassification.Fresh)
            .OrderBy(n => n.Sequence)
            .Select(n => new { Number = n.NormalizedNumber!, PhoneNumberId = n.PhoneNumberId!.Value })
            .ToListAsync(cancellationToken);

        var report = CheckNumberReconciler.Reconcile(submitted.Select(s => s.Number).ToList(), rows.Rows);
        var phoneIds = submitted.ToDictionary(s => s.Number, s => s.PhoneNumberId, StringComparer.Ordinal);
        var now = time.GetUtcNow();

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var hadResults = await db.CheckNumberResults.AnyAsync(r => r.CheckNumberTaskId == task.Id, cancellationToken);
            if (hadResults)
            {
                await db.CheckNumberResults.Where(r => r.CheckNumberTaskId == task.Id).ExecuteDeleteAsync(cancellationToken);
            }

            var results = report.Numbers.Select(n => new CheckNumberResult
            {
                TenantId = job.TenantId,
                CheckNumberTaskId = task.Id,
                JobId = job.Id,
                PhoneNumberId = phoneIds[n.SubmittedNumber],
                SubmittedNumber = n.SubmittedNumber,
                ReturnedNumber = n.ReturnedNumber,
                ActivatedRaw = Text.Truncate(n.ActivatedRaw, 20),
                WhatsAppStatus = n.WhatsAppStatus,
                ValidationStatus = n.ValidationStatus,
                Note = Text.Truncate(n.Note, 1000),
                CheckedAtUtc = now,
            }).ToList();

            foreach (var chunk in results.Chunk(2000))
            {
                db.CheckNumberResults.AddRange(chunk);
                await db.SaveChangesAsync(cancellationToken);

                foreach (var entry in db.ChangeTracker.Entries<CheckNumberResult>().ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }

            await registry.ApplyAnswersAsync(
                results.Select(r => new RegistryAnswer(r.PhoneNumberId, r.WhatsAppStatus, r.Id)).ToList(),
                job.TenantId, now, countAsNewCheck: !hadResults, cancellationToken);

            resultFile.ResultSource = rows.Source;
            resultFile.ParsedRowCount = rows.Rows.Count;
            resultFile.MatchedCount = report.Matched;
            resultFile.RewrittenCount = report.Rewritten;
            resultFile.MissingCount = report.Missing;
            resultFile.UnmatchedRowCount = report.UnmatchedRows;
            resultFile.UpdatedAtUtc = now;

            task.State = CheckNumberTaskState.Processed;
            task.CompletedAtUtc = now;
            task.UpdatedAtUtc = now;

            job.ActualCost = allTasks.Where(t => t.ActualAmount.HasValue).Sum(t => t.ActualAmount!.Value);
            job.CostCurrency ??= task.Currency;
            job.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var message = $"Task {task.ProviderTaskId}: {report.Yes} on WhatsApp, {report.No} not on WhatsApp, {report.Unknown} unverified "
                      + $"({rows.Rows.Count} row(s) read from {rows.Source}, {report.Rewritten} rewritten, {report.Missing} missing, "
                      + $"{report.UnmatchedRows} unmatched).";
        logger.LogInformation("Job {JobNumber}: {Message}", job.JobNumber, message);

        await journal.StepFinishedAsync(stepId, StepStatus.Succeeded, message,
            details: new { report.Yes, report.No, report.Unknown, report.Rewritten, report.Missing, report.UnmatchedRows, rows.Source },
            cancellationToken: cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.ResultsProcessed,
            Message = message,
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = StepCodes.CheckNumberProcessResults,
        }, cancellationToken);

        if (report.Rewritten > 0)
        {
            var example = report.Numbers.First(n => n.ValidationStatus == ResultValidationStatus.Rewritten);
            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.NumbersRewritten,
                Level = EventLevel.Warning,
                Message =
                    $"CheckNumber.ai changed {report.Rewritten} number(s) before checking them (e.g. {example.SubmittedNumber} was checked as "
                    + $"{example.ReturnedNumber}); their answers are marked unverified. Set CheckNumber:SplitTasksByCountry=true to check "
                    + "each country in its own task.",
                TenantId = job.TenantId,
                JobId = job.Id,
                StepCode = StepCodes.CheckNumberProcessResults,
            }, cancellationToken);
        }
    }

    private static DateTimeOffset? Min(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is null || candidate < current ? candidate : current;
}
