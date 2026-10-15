using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One run of the pipeline for one input file (table <c>int_integration_jobs</c>):
/// parse -> CheckNumber.ai -> identify WhatsApp numbers -> WATI -> completed output.
///
/// THE JOB IS THE UNIT OF RESUMPTION. Its <see cref="Status"/> says which stage it is in, and
/// every stage is written so that running it again after a crash does no harm: a stage either
/// finds its work already done in the database (task id stored, archive downloaded, messages
/// accepted) and moves on, or redoes work that has no side effect outside the database.
///
/// THE LEASE (<see cref="LockedBy"/> / <see cref="LockedUntilUtc"/>) is what stops two worker
/// instances working on the same job. A worker claims a job with SELECT ... FOR UPDATE SKIP
/// LOCKED and keeps renewing the lease while it works; if it dies the lease simply expires and
/// another instance carries on from the stored status.
/// </summary>
public sealed class IntegrationJob : Entity, ITenantScoped
{
    /// <summary>Human-readable, unique: INT-yyyyMMdd-NNNNNN.</summary>
    public string JobNumber { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public Guid InputFileId { get; set; }

    public InputFile? InputFile { get; set; }

    /// <summary>Copied from the input file so campaign screens can query jobs directly.</summary>
    public Guid? CampaignId { get; set; }

    public IntegrationJobStatus Status { get; set; } = IntegrationJobStatus.Queued;

    /// <summary>The step currently running or last run (see <c>StepCodes</c>).</summary>
    public string? CurrentStep { get; set; }

    // ---- What this job was asked to do (captured at creation from configuration + manifest) ----

    /// <summary>Send fresh numbers to CheckNumber.ai. When false, only saved answers are used.</summary>
    public bool RunCheckNumber { get; set; } = true;

    /// <summary>
    /// RETIRED 15 Oct 2026: a check never messages anybody, so this is false on every new job. True
    /// only on older jobs whose check sent its own WATI message to every verified number.
    /// </summary>
    public bool RunWati { get; set; }

    /// <summary>Check every number again, ignoring saved answers ("check them again anyway").</summary>
    public bool ForceRecheck { get; set; }

    /// <summary>Older jobs only (see RunWati): the template their automatic send used. Null on new jobs.</summary>
    public string? WatiTemplateName { get; set; }

    /// <summary>Older jobs only (see RunWati): the broadcast name of their automatic send. Null on new jobs.</summary>
    public string? WatiBroadcastName { get; set; }

    /// <summary>Older jobs only (see RunWati): the values of their automatic send, JSON array of {name, value}.</summary>
    public string? WatiParametersJson { get; set; }

    // ---- What the file held ---------------------------------------------------------------

    /// <summary>Rows (or lines) that contained anything at all, across every sheet.</summary>
    public int TotalRows { get; set; }

    /// <summary>Valid numbers found, repeats included.</summary>
    public int NumbersFound { get; set; }

    /// <summary>Distinct valid numbers.</summary>
    public int UniqueNumbers { get; set; }

    /// <summary>Distinct numbers sent to CheckNumber.ai (no saved answer, or a forced recheck).</summary>
    public int FreshNumbers { get; set; }

    /// <summary>Distinct numbers answered from an earlier check - not sent, not paid for.</summary>
    public int ReusedNumbers { get; set; }

    /// <summary>Occurrences of a number already seen earlier in the same file.</summary>
    public int DuplicateNumbers { get; set; }

    /// <summary>Lines with no usable phone number (a heading line with no digits is not counted).</summary>
    public int InvalidLines { get; set; }

    // ---- Progress of the CheckNumber.ai stage ---------------------------------------------------

    /// <summary>Fresh numbers CheckNumber.ai has finished (success + failure from gettasks).</summary>
    public int CheckedNumbers { get; set; }

    // ---- Outcome --------------------------------------------------------------------------------

    /// <summary>Distinct numbers on WhatsApp (fresh answers plus saved answers).</summary>
    public int WhatsAppYes { get; set; }

    public int WhatsAppNo { get; set; }

    /// <summary>Distinct numbers CheckNumber.ai could not verify.</summary>
    public int WhatsAppUnknown { get; set; }

    /// <summary>Older jobs only: verified numbers that qualified for their automatic WATI message. Sends made from a check's numbers are counted on the send (int_wati_jobs), not here.</summary>
    public int WatiEligible { get; set; }

    /// <summary>Older jobs only: messages of the automatic send that WATI accepted.</summary>
    public int WatiSent { get; set; }

    /// <summary>Older jobs only: messages of the automatic send that failed.</summary>
    public int WatiFailed { get; set; }

    /// <summary>Older jobs only: numbers the automatic send skipped, each with a reason.</summary>
    public int WatiSkipped { get; set; }

    /// <summary>What CheckNumber.ai estimated when the task(s) were created.</summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>What CheckNumber.ai actually charged (actual_amount).</summary>
    public decimal? ActualCost { get; set; }

    public string? CostCurrency { get; set; }

    /// <summary>One sentence for the report: why the job succeeded, failed or stopped.</summary>
    public string? OutcomeMessage { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    // ---- Scheduling, locking and restart-safety --------------------------------------------------

    /// <summary>How many times a worker has started processing this job (restarts and retries).</summary>
    public int ProcessingAttempts { get; set; }

    /// <summary>Consecutive transient failures of the current stage (reset on progress).</summary>
    public int ConsecutiveFailures { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>Do not pick the job up before this time (polling interval, retry back-off).</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    /// <summary>Set by a CancelJob command; the processor stops at the next safe point.</summary>
    public bool CancelRequested { get; set; }

    // ---- Who and when ------------------------------------------------------------------------------

    public Guid? CreatedByUserId { get; set; }

    public string? CreatedByName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    // ---- Completed output ----------------------------------------------------------------------------

    /// <summary>Folder (relative to the data root) holding the original file, CheckNumber files and output.</summary>
    public string? WorkingDirectory { get; set; }

    public string? ResultsCsvPath { get; set; }

    public string? SummaryJsonPath { get; set; }

    /// <summary>PostgreSQL xmin, used as an optimistic-concurrency token.</summary>
    public uint RowVersion { get; set; }

    public List<InputFileNumber> Numbers { get; set; } = [];

    public List<CheckNumberTask> CheckNumberTasks { get; set; } = [];

    public List<WatiJob> WatiJobs { get; set; } = [];

    public List<JobStepExecution> Steps { get; set; } = [];

    public bool IsTerminal => JobStatusRules.IsTerminal(Status);
}
