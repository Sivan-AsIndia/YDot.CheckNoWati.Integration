namespace YDot.Integration.Application.Options;

/// <summary>
/// WATI settings. Section <c>Wati</c>. TenantId, ApiToken and ChannelNumber belong in .env
/// (Wati__TenantId, Wati__ApiToken, Wati__ChannelNumber).
/// </summary>
public sealed class WatiOptions
{
    public const string SectionName = "Wati";

    /// <summary>e.g. https://live-mt-server.wati.io (the tenant id is appended to every path).</summary>
    public string BaseUrl { get; set; } = "https://live-mt-server.wati.io";

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Sent as "Authorization: Bearer ...".</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>The WhatsApp sender number registered in WATI, digits only.</summary>
    public string ChannelNumber { get; set; } = string.Empty;

    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Receivers per sendTemplateMessages call (430 numbers -> 100, 100, 100, 100, 30).</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Pause between batches, to stay well inside WATI's rate limits.</summary>
    public int DelayBetweenBatchesMilliseconds { get; set; } = 1000;

    /// <summary>Template used when a job does not name one. Never hard-coded in the code.</summary>
    public string DefaultTemplateName { get; set; } = string.Empty;

    /// <summary>Broadcast name used when a job does not name one.</summary>
    public string DefaultBroadcastName { get; set; } = string.Empty;

    /// <summary>Template parameters used when a job does not supply them (e.g. image_url).</summary>
    public List<TemplateParameterOption> DefaultParameters { get; set; } = [];

    /// <summary>
    /// SAFETY CAP: at most this many messages per WATI job; the rest are recorded as
    /// MAX_RECIPIENTS_REACHED. 0 = unlimited. Kept low by default so a test can never message
    /// hundreds of real people by accident - raise it deliberately for production.
    /// </summary>
    public int MaxRecipientsPerJob { get; set; } = 5;

    /// <summary>
    /// TEST MODE: when not empty, only these numbers (digits, with country code) are ever messaged;
    /// every other verified number is recorded as NOT_IN_ALLOWLIST.
    /// </summary>
    public List<string> AllowedNumbers { get; set; } = [];

    /// <summary>Do not send the same template to the same number for the same campaign twice.</summary>
    public bool SkipAlreadyMessagedNumbers { get; set; } = true;

    /// <summary>With SkipAlreadyMessagedNumbers: only look back this many days. 0 = forever.</summary>
    public int DuplicateWindowDays { get; set; }

    /// <summary>Message numbers CheckNumber.ai could not verify. Off: only confirmed WhatsApp numbers are messaged.</summary>
    public bool SendToUnverifiedNumbers { get; set; }

    /// <summary>
    /// When a send timed out and WATI has no record of a message, send it again. Off by default:
    /// such messages are marked failed (WATI_DELIVERY_UNCERTAIN) for a person to retry, because
    /// resending blindly risks a duplicate message.
    /// </summary>
    public bool ResendWhenDeliveryUnknown { get; set; }

    public WatiStatusSyncOptions StatusSync { get; set; } = new();

    public WatiTemplateSyncOptions TemplateSync { get; set; } = new();

    /// <summary>
    /// Templates offered on the Wati page while WATI's template list cannot be read (and labels /
    /// default values for templates that WATI does return).
    /// </summary>
    public List<ConfiguredTemplateOption> Templates { get; set; } = [];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ApiToken)
        && !string.IsNullOrWhiteSpace(ChannelNumber);
}

/// <summary>A template parameter (WATI customParams entry).</summary>
public sealed class TemplateParameterOption
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>A template declared in configuration.</summary>
public sealed class ConfiguredTemplateOption
{
    /// <summary>WATI elementName.</summary>
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>UTILITY or MARKETING.</summary>
    public string Category { get; set; } = "MARKETING";

    /// <summary>Body with {{parameter}} placeholders, used for the preview.</summary>
    public string Body { get; set; } = string.Empty;

    public List<TemplateParameterOption> Parameters { get; set; } = [];
}

/// <summary>Following each message to Sent / Delivered / Read by polling WATI.</summary>
public sealed class WatiStatusSyncOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker looks for messages due a status check.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>First check this long after WATI accepted the message.</summary>
    public int FirstCheckAfterSeconds { get; set; } = 20;

    /// <summary>Wait between checks of the same message (doubles as it ages, up to MaxCheckIntervalMinutes).</summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    public int MaxCheckIntervalMinutes { get; set; } = 30;

    /// <summary>Messages checked per round.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Stop following a message this long after it was sent.</summary>
    public int MaxTrackingHours { get; set; } = 72;
}

/// <summary>Copying WATI's approved templates into int_wati_templates.</summary>
public sealed class WatiTemplateSyncOptions
{
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;
}
