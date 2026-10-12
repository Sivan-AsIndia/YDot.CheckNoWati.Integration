namespace YDot.Integration.Application.Options;

/// <summary>
/// Which parts of the pipeline run. Section <c>Pipeline</c>.
///
///   EnableCheckNumber = true,  EnableWati = true   check numbers, then message the WhatsApp ones
///   EnableCheckNumber = true,  EnableWati = false  only check numbers (results kept for later)
///   EnableCheckNumber = false, EnableWati = true   only reuse saved answers and message those
///
/// An upload can narrow these (the manifest may ask for no WATI send) but never widen them:
/// a stage switched off here stays off for every job.
/// </summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Send fresh numbers to CheckNumber.ai (billed).</summary>
    public bool EnableCheckNumber { get; set; } = true;

    /// <summary>Allow WATI messages at all (automatic and manual).</summary>
    public bool EnableWati { get; set; } = true;

    /// <summary>After a check, message the verified numbers automatically with the default (or job) template.</summary>
    public bool AutoSendWati { get; set; } = true;

    /// <summary>Allow broadcasts requested from the Wati page.</summary>
    public bool AllowManualWatiBroadcasts { get; set; } = true;
}
