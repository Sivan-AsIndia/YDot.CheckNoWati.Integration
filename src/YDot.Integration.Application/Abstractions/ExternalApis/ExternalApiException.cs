using System.Net;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Abstractions.ExternalApis;

/// <summary>
/// A failed call to CheckNumber.ai, its result storage or WATI, with everything the retry policy
/// needs to decide what to do next.
///
/// THREE KINDS OF FAILURE ARE DISTINGUISHED, because they need three different reactions:
///
///   permanent   400, 401, 402, 403, 404 ...  never retried - the same request fails the same way
///   transient   408, 429, 5xx, timeouts, dropped connections - retried with back-off
///   request not sent   the connection failed before the request left (DNS, refused, TLS) - safe
///               to retry even for billed or non-idempotent calls, because the server never saw it
/// </summary>
public sealed class ExternalApiException : Exception
{
    private const int MaxBodyLength = 2000;

    public ExternalApiException(
        ApiProvider provider,
        string operation,
        string message,
        int? statusCode = null,
        string? responseBody = null,
        bool isTransient = false,
        bool requestNotSent = false,
        bool isTimeout = false,
        TimeSpan? retryAfter = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Provider = provider;
        Operation = operation;
        StatusCode = statusCode;
        ResponseBody = responseBody is { Length: > MaxBodyLength } ? responseBody[..MaxBodyLength] : responseBody;
        IsTransient = isTransient;
        RequestNotSent = requestNotSent;
        IsTimeout = isTimeout;
        RetryAfter = retryAfter;
    }

    public ApiProvider Provider { get; }

    /// <summary>CreateTask, GetTask, DownloadResult, SendTemplateMessages ...</summary>
    public string Operation { get; }

    /// <summary>HTTP status, or null when no response arrived.</summary>
    public int? StatusCode { get; }

    public string? ResponseBody { get; }

    /// <summary>Likely to succeed if tried again later.</summary>
    public bool IsTransient { get; }

    /// <summary>The request never reached the server.</summary>
    public bool RequestNotSent { get; }

    public bool IsTimeout { get; }

    /// <summary>The server's Retry-After, when it sent one.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Safe to retry a call that has a side effect (creating a billed task, sending a message):
    /// only when the request never arrived, or the server explicitly refused it without
    /// processing it (429 Too Many Requests, 503 Service Unavailable).
    /// </summary>
    public bool IsSafeToRetryWithSideEffects =>
        RequestNotSent || StatusCode is (int)HttpStatusCode.TooManyRequests or (int)HttpStatusCode.ServiceUnavailable;

    /// <summary>The request may have been processed even though no success came back.</summary>
    public bool OutcomeUncertain => !RequestNotSent && (IsTimeout || StatusCode is null or 408 or 500 or 502 or 504);

    public bool IsAuthenticationFailure => StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden;

    /// <summary>Classifies an HTTP status the way every client does.</summary>
    public static bool IsTransientStatus(int statusCode) =>
        statusCode is 408 or 429 or 500 or 502 or 503 or 504;
}
