using System.Text.RegularExpressions;

namespace YDot.Integration.Application.Wati;

/// <summary>
/// Fills a template body's {{placeholders}} with parameter values, for the preview stored on a
/// WATI job and shown on the Wati page. A placeholder without a value is left as it is, so a
/// missing value is visible rather than silently blank.
/// </summary>
public static partial class WatiTemplateRenderer
{
    public static string? Render(string? body, IReadOnlyList<TemplateParameter> parameters)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        var values = parameters
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

        return Placeholder().Replace(body, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : match.Value);
    }

    /// <summary>The parameter names a body uses, in order of first appearance.</summary>
    public static IReadOnlyList<string> PlaceholderNames(string? body) =>
        string.IsNullOrEmpty(body)
            ? []
            : Placeholder().Matches(body).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    [GeneratedRegex(@"\{\{\s*([\w.\-]+)\s*\}\}")]
    private static partial Regex Placeholder();
}
