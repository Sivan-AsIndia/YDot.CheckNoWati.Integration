using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Wati;

/// <summary>
/// Keeps int_wati_templates in step with WATI (GET /{tenant}/api/v1/getMessageTemplates), so the
/// Wati page offers the organisation's real, approved templates with their real body and
/// parameters.
///
/// Templates declared in Wati:Templates are always present too (Source = Configuration), so the
/// page has something to offer while WATI cannot be reached; when WATI returns a template of the
/// same name, the WATI copy wins and the configured label and default values are kept as the
/// friendly name and pre-filled values.
/// </summary>
/// <summary>What one template sync achieved - reported back to whoever asked for it.</summary>
/// <param name="ReadFromWati">False when WATI is not configured or could not be read.</param>
/// <param name="Problem">Why WATI could not be read, when it could not.</param>
public sealed record TemplateSyncResult(bool ReadFromWati, int Received, int Approved, int Offered, string? Problem)
{
    /// <summary>One sentence a person can act on.</summary>
    public string Describe() => ReadFromWati
        ? $"WATI templates refreshed: {Received} received, {Approved} approved; {Offered} can be sent."
        : $"WATI could not be read ({Problem ?? "WATI is not configured"}); the {Offered} configured template(s) are offered meanwhile.";
}

public sealed class WatiTemplateSynchronizer(
    IIntegrationDbContext db,
    IWatiClient client,
    IDistributedLock locks,
    IOptions<WatiOptions> options,
    TimeProvider time,
    ILogger<WatiTemplateSynchronizer> logger)
{
    public async Task<TemplateSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        await using var handle = await locks.TryAcquireAsync("ydot-integration:wati-template-sync", cancellationToken);
        if (handle is null)
        {
            // Another instance is syncing right now; its result is what the page will show.
            var offered = await db.WatiTemplates.CountAsync(t => t.IsActive, cancellationToken);
            return new TemplateSyncResult(false, 0, 0, offered, "another worker instance is refreshing the templates right now");
        }

        var settings = options.Value;
        var now = time.GetUtcNow();
        var configured = settings.Templates
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var existing = await db.WatiTemplates.ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(t => t.ElementName, StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<WatiTemplateInfo>? fromWati = null;
        string? problem = null;
        if (client.IsConfigured)
        {
            try
            {
                fromWati = await client.GetTemplatesAsync(ApiCallContext.None, cancellationToken);
            }
            catch (ExternalApiException exception)
            {
                problem = exception.Message;
                logger.LogWarning("WATI templates could not be read ({Reason}); the configured templates are offered meanwhile.", exception.Message);
            }
        }

        if (fromWati is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in fromWati.Where(t => !string.IsNullOrWhiteSpace(t.ElementName)))
            {
                seen.Add(template.ElementName);
                configured.TryGetValue(template.ElementName, out var config);

                if (!byName.TryGetValue(template.ElementName, out var row))
                {
                    row = new WatiTemplate { ElementName = template.ElementName, CreatedAtUtc = now };
                    db.WatiTemplates.Add(row);
                    byName[row.ElementName] = row;
                }

                var defaults = config?.Parameters.Select(p => new TemplateParameter(p.Name, p.Value)) ?? [];
                row.DisplayLabel = FirstNonBlank(config?.Label, Prettify(template.ElementName));
                row.Category = template.Category;
                row.Status = template.Status;
                row.Language = template.Language;
                row.Body = template.Body;
                row.HeaderJson = template.HeaderJson;
                row.Footer = template.Footer;
                row.ButtonsJson = template.ButtonsJson;
                row.ParametersJson = TemplateParameter.ToJson(TemplateParameter.Merge(template.Parameters, defaults));
                row.Source = TemplateSource.Wati;
                row.IsActive = string.Equals(template.Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
                row.LastSyncedAtUtc = now;
                row.UpdatedAtUtc = now;
            }

            // A template deleted in WATI is withdrawn from the page (the row stays for history).
            foreach (var stale in existing.Where(t => t.Source == TemplateSource.Wati && !seen.Contains(t.ElementName) && t.IsActive))
            {
                stale.IsActive = false;
                stale.UpdatedAtUtc = now;
            }

            logger.LogInformation("WATI templates synced: {Count} received, {Approved} approved.",
                fromWati.Count, fromWati.Count(t => string.Equals(t.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)));
        }

        // Configured templates WATI did not supply (or WATI could not be read).
        foreach (var (name, config) in configured)
        {
            if (byName.TryGetValue(name, out var row) && row.Source == TemplateSource.Wati && fromWati is not null)
            {
                continue;
            }

            if (row is null)
            {
                row = new WatiTemplate { ElementName = name, CreatedAtUtc = now };
                db.WatiTemplates.Add(row);
                byName[name] = row;
            }
            else if (row.Source == TemplateSource.Wati)
            {
                continue; // keep the last copy WATI gave us while WATI is unreachable
            }

            row.DisplayLabel = FirstNonBlank(config.Label, Prettify(name));
            row.Category = config.Category;
            row.Status = "CONFIGURED";
            row.Body = config.Body;
            row.ParametersJson = TemplateParameter.ToJson(config.Parameters.Select(p => new TemplateParameter(p.Name, p.Value)));
            row.Source = TemplateSource.Configuration;
            row.IsActive = true;
            row.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new TemplateSyncResult(
            fromWati is not null,
            fromWati?.Count ?? 0,
            fromWati?.Count(t => string.Equals(t.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)) ?? 0,
            byName.Values.Count(t => t.IsActive),
            problem);
    }

    /// <summary>"donation_thank_you_v2" -> "Donation thank you v2".</summary>
    private static string Prettify(string elementName)
    {
        var words = elementName.Replace('_', ' ').Replace('-', ' ').Trim();
        return words.Length == 0 ? elementName : char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
