using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One WATI send run (table <c>int_wati_jobs</c>): a template, a broadcast name, parameters and
/// a set of recipients, sent in batches.
///
/// It is created either AUTOMATICALLY by the pipeline (after CheckNumber.ai verified the numbers
/// of an uploaded file - <see cref="IntegrationJobId"/> is set) or MANUALLY from the Wati page
/// (a person picked recipients and a template - <see cref="CommandId"/> is set). Both kinds are
/// sent by the same dispatcher, under the same safety rules (allow-list, recipient cap,
/// "already sent" check), so a manual broadcast can never bypass them.
/// </summary>
public sealed class WatiJob : Entity, ITenantScoped
{
    /// <summary>Human-readable, unique: WAT-yyyyMMdd-NNNNNN.</summary>
    public string Reference { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public WatiJobSource Source { get; set; }

    /// <summary>The integration job that produced the recipients (automatic jobs).</summary>
    public Guid? IntegrationJobId { get; set; }

    public IntegrationJob? IntegrationJob { get; set; }

    /// <summary>The web-app command that asked for it (manual broadcasts).</summary>
    public Guid? CommandId { get; set; }

    /// <summary>The campaign the message is about (cam_campaigns.id), when known.</summary>
    public Guid? CampaignId { get; set; }

    public WatiJobStatus Status { get; set; } = WatiJobStatus.Queued;

    /// <summary>The approved WATI template (elementName), e.g. "l4l_001".</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Display label of the template at the time of sending.</summary>
    public string? TemplateLabel { get; set; }

    /// <summary>The broadcast name WATI groups the messages under, e.g. "l4l_001".</summary>
    public string BroadcastName { get; set; } = string.Empty;

    /// <summary>Parameters applied to every recipient, JSON array of {name, value}.</summary>
    public string ParametersJson { get; set; } = "[]";

    /// <summary>The template body with the parameters filled in, for the report.</summary>
    public string? RenderedPreview { get; set; }

    /// <summary>The WATI channel (sender) number used.</summary>
    public string? ChannelNumber { get; set; }

    public int BatchSize { get; set; }

    public int TotalRecipients { get; set; }

    public int QueuedCount { get; set; }

    /// <summary>Messages WATI accepted.</summary>
    public int AcceptedCount { get; set; }

    public int FailedCount { get; set; }

    public int SkippedCount { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public string? RequestedByName { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public int ProcessingAttempts { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>PostgreSQL xmin, used as an optimistic-concurrency token.</summary>
    public uint RowVersion { get; set; }

    public List<WatiBatch> Batches { get; set; } = [];

    public List<WatiMessage> Messages { get; set; } = [];

    public bool IsTerminal => Status is WatiJobStatus.Completed or WatiJobStatus.CompletedWithErrors
        or WatiJobStatus.Failed or WatiJobStatus.Cancelled;
}
