using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Common;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;
using YDot.Integration.Infrastructure.Persistence;

namespace YDot.Integration.Infrastructure.Diagnostics;

/// <summary>
/// Writes int_job_steps and int_job_events through its OWN short-lived context, so a step's record
/// commits even when the step's own unit of work rolls back. Never throws: a failed audit write is
/// logged and the job carries on.
/// </summary>
internal sealed class JobJournal(
    IDbContextFactory<IntegrationDbContext> contexts,
    IWorkerIdentity identity,
    TimeProvider time,
    ILogger<JobJournal> logger) : IJobJournal
{
    public async Task<Guid> StepStartedAsync(Guid jobId, string stepCode, string? message, CancellationToken cancellationToken = default)
    {
        var step = new JobStepExecution
        {
            JobId = jobId,
            StepCode = stepCode,
            Status = StepStatus.Running,
            StartedAtUtc = time.GetUtcNow(),
            Message = Text.Truncate(message, 2000),
            WorkerInstance = identity.InstanceName,
        };

        try
        {
            await using var context = await contexts.CreateDbContextAsync(cancellationToken);
            step.Attempt = await context.JobSteps.CountAsync(s => s.JobId == jobId && s.StepCode == stepCode, cancellationToken) + 1;
            context.JobSteps.Add(step);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not record the start of step {Step} for job {JobId}.", stepCode, jobId);
        }

        logger.LogDebug("Job {JobId}: step {Step} started (attempt {Attempt}). {Message}", jobId, stepCode, step.Attempt, message);
        return step.Id;
    }

    public async Task StepFinishedAsync(
        Guid stepExecutionId,
        StepStatus status,
        string? message,
        string? errorCode = null,
        string? errorMessage = null,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contexts.CreateDbContextAsync(cancellationToken);
            var step = await context.JobSteps.SingleOrDefaultAsync(s => s.Id == stepExecutionId, cancellationToken);
            if (step is null)
            {
                return;
            }

            var now = time.GetUtcNow();
            step.Status = status;
            step.CompletedAtUtc = now;
            step.DurationMs = (long)(now - step.StartedAtUtc).TotalMilliseconds;
            step.Message = Text.Truncate(message ?? step.Message, 2000);
            step.ErrorCode = errorCode;
            step.ErrorMessage = Text.Truncate(errorMessage, 4000);
            step.DetailsJson = JsonDefaults.SerializeOrNull(details);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not record the end of step execution {StepId}.", stepExecutionId);
        }
    }

    public async Task EventAsync(JournalEvent entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contexts.CreateDbContextAsync(cancellationToken);
            context.JobEvents.Add(new JobEvent
            {
                TenantId = entry.TenantId,
                InputFileId = entry.InputFileId,
                JobId = entry.JobId,
                WatiJobId = entry.WatiJobId,
                StepCode = entry.StepCode,
                Level = entry.Level,
                EventCode = entry.EventCode,
                Message = Text.Truncate(entry.Message, 4000)!,
                DataJson = JsonDefaults.SerializeOrNull(entry.Data),
                WorkerInstance = identity.InstanceName,
                OccurredAtUtc = time.GetUtcNow(),
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not record event {EventCode}: {Message}", entry.EventCode, entry.Message);
        }

        logger.LogDebug("Event {EventCode}: {Message}", entry.EventCode, entry.Message);
    }
}
