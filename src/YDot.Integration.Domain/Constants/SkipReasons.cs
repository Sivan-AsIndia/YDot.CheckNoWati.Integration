namespace YDot.Integration.Domain.Constants;

/// <summary>
/// Why a number was NOT sent a WATI message. Stored in int_wati_messages.skip_reason so the
/// database always answers "why did this person not get the message?".
/// </summary>
public static class SkipReasons
{
    /// <summary>CheckNumber.ai says the number is not registered on WhatsApp.</summary>
    public const string WhatsAppNotAvailable = "WHATSAPP_NOT_AVAILABLE";

    /// <summary>CheckNumber.ai could not tell (rewritten or missing in the results).</summary>
    public const string WhatsAppUnverified = "WHATSAPP_UNVERIFIED";

    /// <summary>Wati:AllowedNumbers is set (test mode) and this number is not on it.</summary>
    public const string NotInAllowList = "NOT_IN_ALLOWLIST";

    /// <summary>Wati:MaxRecipientsPerJob was reached; the safety cap stops further sends.</summary>
    public const string MaxRecipientsReached = "MAX_RECIPIENTS_REACHED";

    /// <summary>The same template was already sent to this number for the same campaign.</summary>
    public const string AlreadySent = "ALREADY_SENT";

    /// <summary>The number appears more than once in the same WATI job; it is messaged once.</summary>
    public const string DuplicateInJob = "DUPLICATE_IN_JOB";

    /// <summary>The number is not a WhatsApp-verified number of this organisation (manual broadcast).</summary>
    public const string NotInPool = "NOT_IN_POOL";

    /// <summary>The selected value is not a phone number at all (only ever in int_wati_jobs.excluded_json).</summary>
    public const string InvalidNumber = "INVALID_NUMBER";

    /// <summary>The WATI job was cancelled before this message was sent.</summary>
    public const string Cancelled = "CANCELLED";
}
