namespace YDot.Integration.Domain.Enums;

/// <summary>The life of one WhatsApp template message. Queued and Skipped are ours; the rest follow WATI.</summary>
public enum WatiMessageStatus
{
    /// <summary>Will be sent in the next batch.</summary>
    Queued,

    /// <summary>Deliberately not sent; SkipReason says why (WHATSAPP_NOT_AVAILABLE, NOT_IN_ALLOWLIST, ...).</summary>
    Skipped,

    /// <summary>Part of a batch that is in flight.</summary>
    Sending,

    /// <summary>Accepted by WATI, not yet handed to WhatsApp.</summary>
    Pending,

    /// <summary>Sent to WhatsApp.</summary>
    Sent,

    /// <summary>Delivered to the phone.</summary>
    Delivered,

    /// <summary>Read by the recipient.</summary>
    Read,

    /// <summary>The recipient replied.</summary>
    Replied,

    /// <summary>Rejected by WATI or WhatsApp; FailedCode/FailedReason say why.</summary>
    Failed
}
