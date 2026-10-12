using YDot.Integration.Domain.Common;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One status check of a CheckNumber.ai task (table <c>int_checknumber_task_polls</c>).
///
/// Kept so the progress of a task can be read back afterwards (pending -> processing ->
/// exported, with the success/failure counts at each check) and so a slow or stuck task can be
/// shown to CheckNumber.ai support with exact timings.
/// </summary>
public sealed class CheckNumberTaskPoll : Entity
{
    public Guid CheckNumberTaskId { get; set; }

    public Guid JobId { get; set; }

    /// <summary>1-based poll number within the task.</summary>
    public int PollNumber { get; set; }

    public DateTimeOffset PolledAtUtc { get; set; }

    /// <summary>HTTP status of the gettasks call; null when the request never got an answer.</summary>
    public int? HttpStatusCode { get; set; }

    public string? ProviderStatus { get; set; }

    public int? Total { get; set; }

    public int? Success { get; set; }

    public int? Failure { get; set; }

    public int? CachedCount { get; set; }

    public bool HasResultUrl { get; set; }

    public long DurationMs { get; set; }

    public string? ErrorMessage { get; set; }
}
