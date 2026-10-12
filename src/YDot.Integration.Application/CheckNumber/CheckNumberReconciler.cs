using YDot.Integration.Application.PhoneNumbers;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.CheckNumber;

/// <summary>The normalised answer for one submitted number.</summary>
public sealed record ReconciledNumber(
    string SubmittedNumber,
    string? ReturnedNumber,
    string? ActivatedRaw,
    WhatsAppStatus WhatsAppStatus,
    ResultValidationStatus ValidationStatus,
    string? Note);

/// <summary>Every submitted number's answer, plus the rows nothing matched.</summary>
public sealed record ReconciliationReport(IReadOnlyList<ReconciledNumber> Numbers, int UnmatchedRows)
{
    public int Yes => Numbers.Count(n => n.WhatsAppStatus == WhatsAppStatus.Yes);

    public int No => Numbers.Count(n => n.WhatsAppStatus == WhatsAppStatus.No);

    public int Unknown => Numbers.Count(n => n.WhatsAppStatus == WhatsAppStatus.Unknown);

    public int Matched => Numbers.Count(n => n.ValidationStatus is ResultValidationStatus.Checked or ResultValidationStatus.Unrecognised);

    public int Rewritten => Numbers.Count(n => n.ValidationStatus == ResultValidationStatus.Rewritten);

    public int Missing => Numbers.Count(n => n.ValidationStatus == ResultValidationStatus.Missing);
}

/// <summary>
/// Matches CheckNumber.ai's result rows back to the numbers that were submitted (logic proven in
/// the Phase3 worker's ResultReader.Reconcile).
///
/// The service returns digits without "+", and has been seen to put the task's main country code
/// in front of foreign numbers (+14155529876 came back as 9114155529876). Such a row is about a
/// DIFFERENT number, so its answer is not used: the submitted number is reported as Rewritten /
/// Unknown with a note explaining what the service did.
/// </summary>
public static class CheckNumberReconciler
{
    public static ReconciliationReport Reconcile(IReadOnlyList<string> submittedNumbers, IReadOnlyList<ResultRow> rows)
    {
        var rowsByDigits = new Dictionary<string, ResultRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            rowsByDigits.TryAdd(PhoneNumberNormalizer.DigitsOnly(row.Number), row);
        }

        var results = new Dictionary<string, ReconciledNumber>(StringComparer.Ordinal);
        var unresolved = new Dictionary<string, string>(StringComparer.Ordinal); // digits -> submitted number

        foreach (var input in submittedNumbers)
        {
            var digits = PhoneNumberNormalizer.DigitsOnly(input);
            if (rowsByDigits.Remove(digits, out var row))
            {
                results[input] = FromRow(input, row);
            }
            else
            {
                unresolved.TryAdd(digits, input);
            }
        }

        var unmatchedRows = 0;
        foreach (var (returnedDigits, row) in rowsByDigits)
        {
            if (FindRewrittenInput(returnedDigits, unresolved) is not { } originalDigits)
            {
                unmatchedRows++;
                continue;
            }

            var input = unresolved[originalDigits];
            unresolved.Remove(originalDigits);
            var prefix = returnedDigits[..^originalDigits.Length];

            results[input] = new ReconciledNumber(
                input,
                row.Number,
                row.Activated,
                WhatsAppStatus.Unknown,
                ResultValidationStatus.Rewritten,
                $"CheckNumber.ai put {prefix} in front of this number and checked {row.Number} instead (activated={row.Activated}). That answer is for a different number, so it is not used.");
        }

        var ordered = submittedNumbers
            .Select(number => results.TryGetValue(number, out var result)
                ? result
                : new ReconciledNumber(number, null, null, WhatsAppStatus.Unknown, ResultValidationStatus.Missing,
                    "Missing from the CheckNumber.ai results."))
            .ToList();

        return new ReconciliationReport(ordered, unmatchedRows);
    }

    private static ReconciledNumber FromRow(string input, ResultRow row) =>
        row.Activated.Trim().ToLowerInvariant() switch
        {
            "yes" => new ReconciledNumber(input, row.Number, row.Activated, WhatsAppStatus.Yes, ResultValidationStatus.Checked, null),
            "no" => new ReconciledNumber(input, row.Number, row.Activated, WhatsAppStatus.No, ResultValidationStatus.Checked, null),
            _ => new ReconciledNumber(input, row.Number, row.Activated, WhatsAppStatus.Unknown, ResultValidationStatus.Unrecognised,
                $"Unrecognised activated value '{row.Activated}'."),
        };

    private static string? FindRewrittenInput(string returnedDigits, Dictionary<string, string> unresolved)
    {
        // Country calling codes are 1 to 3 digits long.
        for (var prefixLength = 1; prefixLength <= 3 && prefixLength < returnedDigits.Length; prefixLength++)
        {
            var candidate = returnedDigits[prefixLength..];
            if (unresolved.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
