using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One sendTemplateMessages call (table <c>int_wati_batches</c>). With Wati:BatchSize = 100,
/// 430 verified numbers become batches of 100, 100, 100, 100 and 30.
///
/// The row is committed in <see cref="WatiBatchStatus.Sending"/> BEFORE the request goes out.
/// If the worker stops mid-request, the next run sees a Sending batch, marks it
/// <see cref="WatiBatchStatus.Uncertain"/> and checks each of its messages by localMessageId
/// before deciding whether anything needs sending again - so a restart never sends the same
/// message twice.
/// </summary>
public sealed class WatiBatch : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid WatiJobId { get; set; }

    /// <summary>1-based batch number within the WATI job.</summary>
    public int BatchNumber { get; set; }

    public WatiBatchStatus Status { get; set; } = WatiBatchStatus.Pending;

    public int RecipientCount { get; set; }

    public int AcceptedCount { get; set; }

    public int RejectedCount { get; set; }

    /// <summary>HTTP attempts made for this batch (1 + retries).</summary>
    public int AttemptCount { get; set; }

    public int? HttpStatusCode { get; set; }

    /// <summary>The request body that was sent (jsonb).</summary>
    public string? RequestPayloadJson { get; set; }

    /// <summary>WATI's response body (truncated to a safe size).</summary>
    public string? ResponseBody { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long? DurationMs { get; set; }
}
