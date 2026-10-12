using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One CheckNumber.ai batch task (table <c>int_checknumber_tasks</c>).
///
/// THE ROW IS WRITTEN BEFORE THE CREATE REQUEST IS SENT, in state
/// <see cref="CheckNumberTaskState.Creating"/>. Creating a task is billed and CheckNumber.ai
/// offers no idempotency key, so the order matters: if the worker stops while the request is in
/// flight, the next run finds a Creating row with no <see cref="ProviderTaskId"/> and knows the
/// outcome is uncertain - it marks the task <see cref="CheckNumberTaskState.Uncertain"/> and asks
/// a person to decide, instead of silently paying twice. As soon as CheckNumber.ai answers, the
/// task_id is committed on its own, before anything else happens.
///
/// A job normally has one task. It has several only when the fresh numbers exceed
/// CheckNumber:MaxNumbersPerTask, or CheckNumber:SplitTasksByCountry is on.
/// </summary>
public sealed class CheckNumberTask : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid JobId { get; set; }

    public IntegrationJob? Job { get; set; }

    /// <summary>1-based position of the task within its job.</summary>
    public int Sequence { get; set; }

    /// <summary>
    /// True when a person retried the job and this task was replaced by a new one (it failed, or
    /// its creation was uncertain). Superseded rows are kept for audit and ignored by the pipeline.
    /// </summary>
    public bool IsSuperseded { get; set; }

    /// <summary>CheckNumber.ai's task_id (e.g. "dajr6kmp2jvk15vufn1g"). Unique once known.</summary>
    public string? ProviderTaskId { get; set; }

    /// <summary>"ws" (WhatsApp check) or "ws_active" (activity check).</summary>
    public string TaskType { get; set; } = "ws";

    public CheckNumberTaskState State { get; set; } = CheckNumberTaskState.Creating;

    /// <summary>The raw status CheckNumber.ai last reported (pending / processing / exported ...).</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>Numbers uploaded in this task.</summary>
    public int SubmittedCount { get; set; }

    /// <summary>Calling code of the numbers when tasks are split by country; otherwise null.</summary>
    public string? CountryCode { get; set; }

    public int? Total { get; set; }

    public int? Success { get; set; }

    public int? Failure { get; set; }

    /// <summary>Numbers CheckNumber.ai answered from its own cache.</summary>
    public int? CachedCount { get; set; }

    public decimal? EstimatedAmount { get; set; }

    public decimal? ActualAmount { get; set; }

    public string? Currency { get; set; }

    /// <summary>
    /// The pre-signed S3 link to the result archive. It is a bearer link (valid about 48 hours),
    /// so it is stored for resumption but never written to a log line with its signature.
    /// </summary>
    public string? ResultUrl { get; set; }

    /// <summary>CheckNumber.ai's own message on create, or its error text.</summary>
    public string? ProviderMessage { get; set; }

    /// <summary>The exact upload sent to CheckNumber.ai, relative to the data root.</summary>
    public string? SubmittedNumbersPath { get; set; }

    /// <summary>Status checks made so far (bounded by CheckNumber:MaxPollingAttempts).</summary>
    public int PollCount { get; set; }

    public DateTimeOffset? LastPolledAtUtc { get; set; }

    public DateTimeOffset? NextPollAtUtc { get; set; }

    /// <summary>When the create request was sent (the moment from which the task may exist).</summary>
    public DateTimeOffset? CreateRequestedAtUtc { get; set; }

    /// <summary>created_at as reported by CheckNumber.ai.</summary>
    public DateTimeOffset? ProviderCreatedAtUtc { get; set; }

    /// <summary>updated_at as reported by CheckNumber.ai.</summary>
    public DateTimeOffset? ProviderUpdatedAtUtc { get; set; }

    public DateTimeOffset? ExportedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<CheckNumberTaskPoll> Polls { get; set; } = [];

    public CheckNumberResultFile? ResultFile { get; set; }

    public List<CheckNumberResult> Results { get; set; } = [];
}
