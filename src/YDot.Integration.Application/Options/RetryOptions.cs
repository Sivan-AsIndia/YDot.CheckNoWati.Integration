namespace YDot.Integration.Application.Options;

/// <summary>
/// Retry with exponential back-off for TRANSIENT failures only (HTTP 408, 429, 500, 502, 503,
/// 504, timeouts, dropped connections). Section <c>Retry</c>.
///
/// With the defaults (3, 2, 60) a failing call is tried once and then retried three times,
/// waiting 2, 4 and 8 seconds: "Retry attempt 1/3 after 2 seconds", "2/3 after 4", "3/3 after 8".
/// A permanent failure (400, 401, 402, 403, 404) is never retried.
/// </summary>
public sealed class RetryOptions
{
    public const string SectionName = "Retry";

    /// <summary>Retries after the first attempt.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Wait before the first retry; each later wait doubles.</summary>
    public int InitialDelaySeconds { get; set; } = 2;

    /// <summary>Upper bound for any single wait (also caps a server's Retry-After).</summary>
    public int MaxDelaySeconds { get; set; } = 60;
}
