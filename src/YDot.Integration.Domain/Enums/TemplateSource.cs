namespace YDot.Integration.Domain.Enums;

/// <summary>Where a row of int_wati_templates came from.</summary>
public enum TemplateSource
{
    /// <summary>Copied from WATI's getMessageTemplates API.</summary>
    Wati,

    /// <summary>Declared in the worker configuration (Wati:Templates), used while WATI cannot be reached.</summary>
    Configuration
}
