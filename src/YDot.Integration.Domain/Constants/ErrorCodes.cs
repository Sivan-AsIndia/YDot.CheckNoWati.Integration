namespace YDot.Integration.Domain.Constants;

/// <summary>
/// Stable error codes written to int_integration_jobs.error_code, int_checknumber_tasks,
/// int_wati_messages and the event log. The web application shows them next to the reason,
/// so they must never be renamed once released - add new ones instead.
/// </summary>
public static class ErrorCodes
{
    // ---- File and parsing ---------------------------------------------------------------
    public const string FileMissing = "FILE_MISSING";
    public const string FileUnreadable = "FILE_UNREADABLE";
    public const string NoValidNumbers = "NO_VALID_NUMBERS";
    public const string BelowMinimumNumbers = "BELOW_MINIMUM_NUMBERS";

    // ---- CheckNumber.ai --------------------------------------------------------------------
    public const string CheckNumberDisabled = "CHECKNUMBER_DISABLED";
    public const string CheckNumberRejected = "CHECKNUMBER_REJECTED";
    public const string CheckNumberCreateUncertain = "CHECKNUMBER_CREATE_UNCERTAIN";
    public const string CheckNumberTaskFailed = "CHECKNUMBER_TASK_FAILED";
    public const string CheckNumberPollTimeout = "CHECKNUMBER_POLL_TIMEOUT";
    public const string CheckNumberUnavailable = "CHECKNUMBER_UNAVAILABLE";
    public const string CheckNumberDownloadFailed = "CHECKNUMBER_DOWNLOAD_FAILED";
    public const string CheckNumberExtractFailed = "CHECKNUMBER_EXTRACT_FAILED";
    public const string CheckNumberResultInvalid = "CHECKNUMBER_RESULT_INVALID";

    // ---- WATI ------------------------------------------------------------------------------
    public const string WatiNotConfigured = "WATI_NOT_CONFIGURED";
    public const string WatiUnauthorized = "WATI_UNAUTHORIZED";
    public const string WatiRejected = "WATI_REJECTED";
    public const string WatiUnavailable = "WATI_UNAVAILABLE";
    public const string WatiDeliveryUncertain = "WATI_DELIVERY_UNCERTAIN";
    public const string WatiInvalidNumber = "WATI_INVALID_NUMBER";
    public const string WatiMessageFailed = "WATI_MESSAGE_FAILED";
    public const string WatiTemplateMissing = "WATI_TEMPLATE_MISSING";

    // ---- General ---------------------------------------------------------------------------
    public const string CancelledByUser = "CANCELLED_BY_USER";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    public const string Unexpected = "UNEXPECTED_ERROR";
}
