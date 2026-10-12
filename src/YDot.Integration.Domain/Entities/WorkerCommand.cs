using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// A request from the YDot web application to the worker (table <c>int_worker_commands</c>):
/// retry a job, cancel a job, send a manual WATI broadcast, refresh templates.
///
/// WHY A COMMAND TABLE. The worker is the only writer of the integration tables, so the WATI
/// rules (allow-list, recipient cap, duplicate check) and the job state machine live in one
/// place. The web application only inserts a Pending command; the worker claims it with
/// SELECT ... FOR UPDATE SKIP LOCKED, carries it out and writes the outcome back, which the
/// screens read.
/// </summary>
public sealed class WorkerCommand : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }

    /// <summary>Human-readable, unique, e.g. BRD-20261010-7K2M9Q.</summary>
    public string Reference { get; set; } = string.Empty;

    public WorkerCommandType CommandType { get; set; }

    public WorkerCommandStatus Status { get; set; } = WorkerCommandStatus.Pending;

    /// <summary>The request details (jsonb).</summary>
    public string PayloadJson { get; set; } = "{}";

    public string? ResultMessage { get; set; }

    /// <summary>The row the command produced or acted on (job id, WATI job id ...).</summary>
    public Guid? ResultEntityId { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public string? RequestedByName { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public int Attempts { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }
}
