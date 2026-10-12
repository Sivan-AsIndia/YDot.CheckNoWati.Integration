namespace YDot.Integration.Domain.Entities;

/// <summary>
/// Daily counters behind the human-readable references (table <c>int_job_sequences</c>):
/// INT-20261010-000001, WAT-20261010-000001 ...
///
/// The next value is taken with a single INSERT ... ON CONFLICT DO UPDATE ... RETURNING, which
/// PostgreSQL executes atomically, so two worker instances can never hand out the same number.
/// </summary>
public sealed class JobSequence
{
    /// <summary>"INT", "WAT", "UPL" ...</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>"yyyyMMdd".</summary>
    public string Period { get; set; } = string.Empty;

    public long LastValue { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
