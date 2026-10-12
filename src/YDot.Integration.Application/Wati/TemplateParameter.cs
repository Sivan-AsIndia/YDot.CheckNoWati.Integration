using System.Text.Json;
using YDot.Integration.Application.Common;

namespace YDot.Integration.Application.Wati;

/// <summary>
/// One WATI template parameter ({"name": "image_url", "value": "https://..."}).
///
/// Stored as a JSON array in jsonb columns (int_wati_jobs.parameters_json,
/// int_wati_messages.parameters_json, int_integration_jobs.wati_parameters_json), so a template
/// with new parameters - a name, an amount, a link - needs no database change.
/// </summary>
public sealed record TemplateParameter(string Name, string Value)
{
    public static string ToJson(IEnumerable<TemplateParameter> parameters) =>
        JsonSerializer.Serialize(parameters.ToList(), JsonDefaults.Options);

    public static IReadOnlyList<TemplateParameter> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<TemplateParameter>>(json, JsonDefaults.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The parameters a job sends: the job's own values override the defaults by name, and
    /// defaults the job does not mention are kept.
    /// </summary>
    public static IReadOnlyList<TemplateParameter> Merge(
        IEnumerable<TemplateParameter> defaults, IEnumerable<TemplateParameter>? overrides)
    {
        var merged = new List<TemplateParameter>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in defaults.Concat(overrides ?? []))
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                continue;
            }

            var cleaned = new TemplateParameter(parameter.Name.Trim(), parameter.Value ?? string.Empty);

            if (index.TryGetValue(cleaned.Name, out var position))
            {
                merged[position] = cleaned;
            }
            else
            {
                index[cleaned.Name] = merged.Count;
                merged.Add(cleaned);
            }
        }

        return merged;
    }
}
