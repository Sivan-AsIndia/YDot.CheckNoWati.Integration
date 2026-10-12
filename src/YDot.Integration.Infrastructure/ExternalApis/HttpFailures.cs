using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Infrastructure.ExternalApis;

/// <summary>Builds <see cref="ExternalApiException"/>s the same way for every external API.</summary>
internal static partial class HttpFailures
{
    public static ExternalApiException FromResponse(ApiProvider provider, string operation, HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;

        // The service's own explanation is the most precise reason; describe the status only when it sends none.
        var detail = ExtractDetail(body) ?? Describe(status, response.ReasonPhrase);

        return new ExternalApiException(
            provider,
            operation,
            $"{operation} failed (HTTP {status}): {detail}",
            status,
            body,
            isTransient: ExternalApiException.IsTransientStatus(status),
            retryAfter: RetryAfter(response));
    }

    public static ExternalApiException FromTransport(ApiProvider provider, string operation, HttpRequestException exception) =>
        new(provider,
            operation,
            $"{operation} failed: {exception.Message}",
            exception.StatusCode is { } status ? (int)status : null,
            isTransient: true,
            requestNotSent: exception.HttpRequestError is HttpRequestError.NameResolutionError
                or HttpRequestError.ConnectionError
                or HttpRequestError.SecureConnectionError
                or HttpRequestError.ProxyTunnelError,
            inner: exception);

    public static ExternalApiException FromTimeout(ApiProvider provider, string operation, TimeSpan timeout, Exception exception) =>
        new(provider, operation, $"{operation} timed out after {timeout.TotalSeconds:0} seconds.", isTransient: true, isTimeout: true, inner: exception);

    public static ExternalApiException Interrupted(ApiProvider provider, string operation, Exception exception) =>
        new(provider, operation, $"{operation} was interrupted: {exception.Message}", isTransient: true, inner: exception);

    /// <summary>Removes the S3 signature and credential from bodies and URLs before they are stored.</summary>
    public static string? RedactSecrets(string? text) =>
        text is null ? null : SignedUrlSecret().Replace(text, "$1<redacted>");

    private static string Describe(int status, string? reasonPhrase) => status switch
    {
        400 => "the request was refused as invalid.",
        401 => "the API key or token is missing or invalid.",
        402 => "insufficient account balance.",
        403 => "access denied (product not enabled for this account, or the link has expired).",
        404 => "not found.",
        429 => "rate limited.",
        _ => $"{reasonPhrase ?? "request failed"}.",
    };

    private static string? ExtractDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in (string[])["message", "error", "detail", "info", "title"])
                {
                    if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return value.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (S3 answers in XML, gateways in HTML): fall back to the raw text.
        }

        var text = body.Trim();
        return text.Length <= 300 ? text : text[..300] + "...";
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } retryAfter)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    [GeneratedRegex(@"(?i)(X-Amz-Signature=|X-Amz-Credential=|X-Amz-Security-Token=)[^&""\s\\]+")]
    private static partial Regex SignedUrlSecret();
}
