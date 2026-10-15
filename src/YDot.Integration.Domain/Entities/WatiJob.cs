using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One WATI send run (table <c>int_wati_jobs</c>): a template, a broadcast name, parameters and
/// a set of recipients, sent in batches.
///
/// SINCE 15 OCTOBER 2026 EVERY NEW ONE IS A PERSON'S SELECTION. A check (CheckNumber.ai) only
/// validates numbers; a WATI job is created when somebody picks numbers on the WhatsApp Messages
/// page, chooses a template and presses Send (<see cref="CommandId"/> is set). Each message row
/// records the check job the number was picked from, so every send can be traced back to the
/// CheckNumber.ai task that verified the number.
///
/// Older rows may be AUTOMATIC: the pipeline used to message every verified number of an upload
/// as soon as the check finished (<see cref="IntegrationJobId"/> is set). Those are kept as
/// history and still finish if one was in flight, but no new one is ever created.
///
/// Both kinds are sent by the same dispatcher, under the same safety rules (allow-list,
/// recipient cap, "already sent" check), so a selection can never bypass them.
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

    /// <summary>
    /// The template's body with its {{placeholders}}, AS IT WAS WHEN THE MESSAGE WENT OUT. WATI
    /// templates can be edited and re-approved later; the report renders each message from this
    /// copy, so it shows what the recipient actually received.
    /// </summary>
    public string? TemplateBody { get; set; }

    /// <summary>The template's header (an image header carries {{image_url}}) at the time of sending (jsonb).</summary>
    public string? TemplateHeaderJson { get; set; }

    /// <summary>
    /// Template parameters filled per recipient rather than with one value for everybody (jsonb,
    /// [{"name":"name","source":"contactName"}]). The value in <see cref="ParametersJson"/> is the
    /// fallback for a recipient the source has nothing for.
    /// </summary>
    public string? ParameterBindingsJson { get; set; }

    /// <summary>How many numbers the person selected - the message rows plus <see cref="ExcludedJson"/>.</summary>
    public int RequestedCount { get; set; }

    /// <summary>
    /// Selected numbers that never became a message row, each with the reason (jsonb,
    /// [{"number":"+91...","reason":"NOT_IN_POOL"}]): not a WhatsApp-verified number of this
    /// organisation, not a phone number at all, or listed twice. Kept so the audit answers "which
    /// numbers did the person choose?" completely, not only "which were sent to".
    /// </summary>
    public string? ExcludedJson { get; set; }

    /// <summary>The recipient group (cam_whatsapp_recipient_groups.id) the selection came from, when it did.</summary>
    public Guid? RecipientGroupId { get; set; }

    /// <summary>The group's name at the time of sending.</summary>
    public string? RecipientGroupName { get; set; }

    /// <summary>Labels for the send, e.g. ["diwali","chennai"] (jsonb), copied from the group or typed on the page.</summary>
    public string? TagsJson { get; set; }

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
