using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Abstractions.Diagnostics;

/// <summary>An event for int_job_events.</summary>
public sealed record JournalEvent
{
    public required string EventCode { get; init; }

    public required string Message { get; init; }

    public EventLevel Level { get; init; } = EventLevel.Information;

    public Guid? TenantId { get; init; }

    public Guid? InputFileId { get; init; }

    public Guid? JobId { get; init; }

    public Guid? WatiJobId { get; init; }

    public string? StepCode { get; init; }

    /// <summary>Any object; serialised to jsonb.</summary>
    public object? Data { get; init; }
}

/// <summary>
/// Writes the job timeline (int_job_steps) and the event narrative (int_job_events), and the
/// matching log line.
///
/// IT COMMITS ON ITS OWN CONNECTION, separately from the step's unit of work, so the record of
/// a step that FAILED survives even when the step's own changes are rolled back - which is
/// exactly when the record matters most. A journal write that fails is logged and swallowed: an
/// audit hiccup must never fail a paid job.
/// </summary>
public interface IJobJournal
{
    /// <summary>Records a step starting and returns the row id to finish it with.</summary>
    Task<Guid> StepStartedAsync(Guid jobId, string stepCode, string? message, CancellationToken cancellationToken = default);

    Task StepFinishedAsync(
        Guid stepExecutionId,
        StepStatus status,
        string? message,
        string? errorCode = null,
        string? errorMessage = null,
        object? details = null,
        CancellationToken cancellationToken = default);

    Task EventAsync(JournalEvent entry, CancellationToken cancellationToken = default);
}
