using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// A WhatsApp message template the organisation can send (table <c>int_wati_templates</c>).
///
/// The worker copies the approved templates from WATI (getMessageTemplates) on a schedule, so
/// the Wati page lists real templates with their real body and parameters instead of a list
/// typed into the browser. While WATI cannot be reached, the templates declared in
/// Wati:Templates are written with <see cref="TemplateSource.Configuration"/> so the page is
/// never empty.
/// </summary>
public sealed class WatiTemplate : Entity
{
    /// <summary>WATI elementName, e.g. "l4l_001". Unique.</summary>
    public string ElementName { get; set; } = string.Empty;

    /// <summary>Name shown on the Wati page.</summary>
    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>UTILITY, MARKETING, AUTHENTICATION ...</summary>
    public string? Category { get; set; }

    /// <summary>APPROVED, PENDING, REJECTED ...</summary>
    public string? Status { get; set; }

    public string? Language { get; set; }

    /// <summary>The template body with its {{placeholders}}.</summary>
    public string? Body { get; set; }

    public string? HeaderJson { get; set; }

    public string? Footer { get; set; }

    public string? ButtonsJson { get; set; }

    /// <summary>JSON array of {name, value}: the parameters and their default values.</summary>
    public string ParametersJson { get; set; } = "[]";

    public TemplateSource Source { get; set; }

    /// <summary>False when the template disappeared from WATI or is not approved.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastSyncedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
