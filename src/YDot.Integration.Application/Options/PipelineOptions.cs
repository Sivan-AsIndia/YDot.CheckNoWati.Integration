namespace YDot.Integration.Application.Options;

/// <summary>
/// Which parts of the pipeline run. Section <c>Pipeline</c>.
///
///   EnableCheckNumber = true,  EnableWati = true   check numbers; message the ones a person selects
///   EnableCheckNumber = true,  EnableWati = false  only check numbers (results kept for later)
///   EnableCheckNumber = false, EnableWati = true   only reuse saved answers; message selections
///
/// A CHECK NEVER MESSAGES ANYBODY BY ITSELF (15 October 2026). WATI sends only the numbers a
/// person picks on the WhatsApp Messages page, so the old "message every verified number when the
/// check finishes" switch is retired (<see cref="AutoSendWati"/>).
/// </summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Send fresh numbers to CheckNumber.ai (billed).</summary>
    public bool EnableCheckNumber { get; set; } = true;

    /// <summary>Allow WATI messages at all.</summary>
    public bool EnableWati { get; set; } = true;

    /// <summary>
    /// RETIRED - read and ignored. It used to message every verified number of an upload as soon as
    /// the check finished: 450 messages for a 500-number file. Numbers are now messaged only when a
    /// person selects them; the worker logs a warning at start-up if this is still set to true.
    /// </summary>
    public bool AutoSendWati { get; set; }

    /// <summary>Allow sends requested from the Wati page (the only way anything is sent).</summary>
    public bool AllowManualWatiBroadcasts { get; set; } = true;
}
