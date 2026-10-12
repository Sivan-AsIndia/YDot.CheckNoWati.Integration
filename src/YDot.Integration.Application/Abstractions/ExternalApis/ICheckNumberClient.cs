namespace YDot.Integration.Application.Abstractions.ExternalApis;

/// <summary>Answer of POST /v1/tasks (HTTP 202).</summary>
public sealed record CheckNumberCreatedTask(
    string TaskId,
    string Status,
    int Total,
    decimal? EstimatedAmount,
    string? Currency,
    string? Message,
    DateTimeOffset? CreatedAt);

/// <summary>Answer of POST /v1/gettasks.</summary>
public sealed record CheckNumberTaskStatus(
    string TaskId,
    string Status,
    int Total,
    int Success,
    int Failure,
    string? ResultUrl,
    int? CachedCount,
    decimal? ActualAmount,
    string? Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    int HttpStatusCode);

/// <summary>What a result download produced.</summary>
public sealed record CheckNumberDownload(int HttpStatusCode, long SizeBytes, string Sha256, long DurationMs);

/// <summary>
/// CheckNumber.ai batch API (verified against the live service and the Phase3 worker):
///
///   POST /v1/tasks      multipart: file (one number per line), task_type=ws  -> task_id
///   POST /v1/gettasks   multipart: task_id                                    -> status, result_url
///   GET  result_url     pre-signed S3 zip (no API key is ever sent to S3)
///
/// Note: GET /v1/gettasks answers 404; the status call is a POST with a form field.
/// Every method records one int_api_call_logs row per attempt.
/// </summary>
public interface ICheckNumberClient
{
    Task<CheckNumberCreatedTask> CreateTaskAsync(
        string fileName, IReadOnlyCollection<string> numbers, ApiCallContext context, int attempt,
        CancellationToken cancellationToken);

    Task<CheckNumberTaskStatus> GetTaskAsync(
        string taskId, ApiCallContext context, int attempt, CancellationToken cancellationToken);

    /// <summary>Downloads to a temporary file and renames it into place, so a half download never looks complete.</summary>
    Task<CheckNumberDownload> DownloadResultAsync(
        string resultUrl, string destinationPath, ApiCallContext context, int attempt, CancellationToken cancellationToken);
}

/// <summary>CheckNumber.ai's documented statuses, and the failure names treated as terminal.</summary>
public static class CheckNumberStatuses
{
    public static bool IsInProgress(string? status) =>
        status?.Trim().ToLowerInvariant() is "pending" or "processing" or "queued" or "running";

    public static bool IsExported(string? status) =>
        string.Equals(status?.Trim(), "exported", StringComparison.OrdinalIgnoreCase);

    public static bool IsFailed(string? status) =>
        status?.Trim().ToLowerInvariant() is "failed" or "error" or "cancelled" or "canceled" or "expired";
}
