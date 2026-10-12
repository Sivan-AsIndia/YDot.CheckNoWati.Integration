using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Options;

/// <summary>
/// CheckNumber.ai settings. Section <c>CheckNumber</c>. The API key belongs in .env
/// (CheckNumber__ApiKey), never in appsettings.json.
/// </summary>
public sealed class CheckNumberOptions
{
    public const string SectionName = "CheckNumber";

    public string BaseUrl { get; set; } = "https://api.checknumber.ai/";

    /// <summary>Sent as the X-API-Key header. Set CheckNumber__ApiKey in .env.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>"ws" = WhatsApp number check, "ws_active" = WhatsApp activity check.</summary>
    public string TaskType { get; set; } = "ws";

    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>Wait after creating a task before the first status check.</summary>
    public int InitialPollDelaySeconds { get; set; } = 30;

    /// <summary>Time between status checks. Polling never runs in a tight loop: the job is parked in between.</summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>Status checks before the task is declared timed out (a retry resumes the same task).</summary>
    public int MaxPollingAttempts { get; set; } = 20;

    /// <summary>1.0 = fixed interval; above 1 each wait grows by this factor up to MaxPollingIntervalSeconds.</summary>
    public double PollingBackoffMultiplier { get; set; } = 1.0;

    public int MaxPollingIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// A file with fewer fresh numbers than this fails before any paid call. CheckNumber.ai accepts
    /// small tasks, but bills at least the price of 500 numbers per task (about 0.045 USD).
    /// </summary>
    public int MinimumNumbersPerTask { get; set; } = 1;

    /// <summary>Fresh numbers above this are split over several tasks.</summary>
    public int MaxNumbersPerTask { get; set; } = 50000;

    /// <summary>
    /// CheckNumber.ai picks ONE country per task and puts its code in front of numbers that lack it,
    /// so foreign numbers in a mostly-Indian file come back as "rewritten" (unverified). True sends
    /// each country's numbers as a separate task - accurate, but each task carries the minimum charge.
    /// </summary>
    public bool SplitTasksByCountry { get; set; }

    /// <summary>Global: a saved answer is reused for every organisation. Tenant: only for the one that paid for it.</summary>
    public ReuseScope ReuseScope { get; set; } = ReuseScope.Global;

    /// <summary>
    /// Saved answers older than this are checked again. 0 = a saved answer never expires.
    /// Only Yes and No are ever reused; a number CheckNumber.ai could not verify is always checked
    /// again (the same rule the upload page applies).
    /// </summary>
    public int ResultValidityDays { get; set; }

    /// <summary>Price per number, used only for the estimate shown before a check (CheckNumber.ai bills actual_amount).</summary>
    public decimal PricePerNumberUsd { get; set; } = 0.00009m;

    /// <summary>Smallest amount CheckNumber.ai bills for one task.</summary>
    public decimal MinimumChargePerTaskUsd { get; set; } = 0.045m;

    /// <summary>Time allowed for one download attempt of the result archive.</summary>
    public int DownloadTimeoutSeconds { get; set; } = 600;

    /// <summary>Zip-bomb guard: refuse an archive that unpacks to more than this.</summary>
    public long MaxExtractedBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>Zip-bomb guard: refuse an archive with more entries than this.</summary>
    public int MaxArchiveEntries { get; set; } = 200;
}
