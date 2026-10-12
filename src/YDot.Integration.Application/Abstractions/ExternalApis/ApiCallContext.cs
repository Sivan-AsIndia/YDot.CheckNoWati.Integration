namespace YDot.Integration.Application.Abstractions.ExternalApis;

/// <summary>
/// Which job / task / batch / message an HTTP call belongs to. The clients copy it onto every
/// int_api_call_logs row, so a call can be traced back to the file it was made for.
/// </summary>
public sealed record ApiCallContext
{
    public Guid? TenantId { get; init; }

    public Guid? JobId { get; init; }

    public Guid? WatiJobId { get; init; }

    /// <summary>The task, batch or message row the call is about.</summary>
    public Guid? RelatedEntityId { get; init; }

    /// <summary>"INT-20261010-000001" or "WAT-20261010-000001", for log lines.</summary>
    public string? JobLabel { get; init; }

    public string? StepCode { get; init; }

    public static ApiCallContext None { get; } = new();
}
