namespace YDot.Integration.Domain.Enums;

/// <summary>Whether a number has WhatsApp, as far as CheckNumber.ai could tell.</summary>
public enum WhatsAppStatus
{
    /// <summary>Not checked yet, or CheckNumber.ai could not tell (for example it checked a rewritten number).</summary>
    Unknown,

    /// <summary>Registered on WhatsApp.</summary>
    Yes,

    /// <summary>Not registered on WhatsApp.</summary>
    No
}
