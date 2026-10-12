using System.Text.RegularExpressions;

namespace YDot.Integration.Application.Common;

/// <summary>Small string helpers used when writing audit rows.</summary>
public static partial class Text
{
    /// <summary>Cuts a value to fit a bounded column, marking that it was cut.</summary>
    public static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        const string marker = "...(truncated)";
        return maxLength <= marker.Length ? value[..maxLength] : value[..(maxLength - marker.Length)] + marker;
    }

    /// <summary>
    /// Removes secrets from a URL before it is stored or logged: the whole query string of a
    /// pre-signed S3 link (X-Amz-Signature, X-Amz-Credential ...) is replaced with "?<redacted>".
    /// </summary>
    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        var query = url.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
        {
            return url;
        }

        var hasSecrets = SecretQueryKey().IsMatch(url[query..]);
        return hasSecrets ? url[..query] + "?<redacted>" : url;
    }

    /// <summary>"0.049500" -> 0.0495m; null for anything that is not a number.</summary>
    public static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    [GeneratedRegex(@"(?i)(signature|credential|token|key|sig)=")]
    private static partial Regex SecretQueryKey();
}
