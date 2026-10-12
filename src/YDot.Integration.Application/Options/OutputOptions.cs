namespace YDot.Integration.Application.Options;

/// <summary>The completed output written for every finished job. Section <c>Output</c>.</summary>
public sealed class OutputOptions
{
    public const string SectionName = "Output";

    /// <summary>data/output/{job}/whatsapp-results.csv: one line per number with every decision taken.</summary>
    public bool WriteResultsCsv { get; set; } = true;

    /// <summary>data/output/{job}/summary.json: counts, task ids, costs, WATI outcome and timings.</summary>
    public bool WriteSummaryJson { get; set; } = true;
}
