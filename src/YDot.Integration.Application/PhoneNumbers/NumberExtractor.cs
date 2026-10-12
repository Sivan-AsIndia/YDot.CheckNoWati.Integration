using YDot.Integration.Application.Abstractions.Files;

namespace YDot.Integration.Application.PhoneNumbers;

/// <summary>One thing the file held: a valid number (first or repeated sighting) or an unusable line.</summary>
public sealed record NumberOccurrence(
    string SheetName,
    int LineNumber,
    string RawValue,
    string? E164,
    bool IsDuplicate,
    string? InvalidReason)
{
    public bool IsInvalid => E164 is null;
}

/// <summary>Per-sheet counts, as the review screen's "Inside this file" table shows them.</summary>
public sealed record SheetSummary(string Name, int Rows, int Found, int Unique, int Duplicates, int Invalid);

/// <summary>Everything extracted from one file, before any saved answer is looked up.</summary>
public sealed record ExtractionResult(
    IReadOnlyList<NumberOccurrence> Occurrences,
    IReadOnlyList<SheetSummary> Sheets,
    int TotalRows,
    int NumbersFound,
    int UniqueNumbers,
    int DuplicateNumbers,
    int InvalidLines)
{
    /// <summary>Distinct valid numbers in the order they first appear.</summary>
    public IEnumerable<string> DistinctNumbers =>
        Occurrences.Where(o => !o.IsInvalid && !o.IsDuplicate).Select(o => o.E164!);
}

/// <summary>
/// Reads every sheet and row of a file and lists what it holds, with the counting rules of the
/// upload page (whatsapp-store.service.ts, analyse):
///
///   - a row with no text at all is skipped (it is not counted as a row);
///   - a number seen earlier in the file (any sheet) is a duplicate;
///   - a first line with no digits at all is a column heading ("Name, Phone"), not a bad line.
/// </summary>
public static class NumberExtractor
{
    public static ExtractionResult Extract(NumberFileContents contents)
    {
        var occurrences = new List<NumberOccurrence>();
        var sheets = new List<SheetSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var totalRows = 0;

        foreach (var sheet in contents.Sheets)
        {
            int rows = 0, found = 0, unique = 0, duplicates = 0, invalid = 0;

            foreach (var row in sheet.Rows)
            {
                if (!row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    continue;
                }

                rows++;
                var scan = RowScanner.Scan(row.Cells);

                foreach (var hit in scan.Numbers)
                {
                    found++;
                    var isDuplicate = !seen.Add(hit.E164);
                    if (isDuplicate)
                    {
                        duplicates++;
                    }
                    else
                    {
                        unique++;
                    }

                    occurrences.Add(new NumberOccurrence(sheet.Name, row.LineNumber, hit.Raw, hit.E164, isDuplicate, null));
                }

                var isHeading = row.LineNumber == 1 && scan.Rejected?.Reason == RowScanner.NoPhoneNumberReason;
                if (scan.Rejected is not null && !isHeading)
                {
                    invalid++;
                    occurrences.Add(new NumberOccurrence(
                        sheet.Name, row.LineNumber, scan.Rejected.Raw, null, false, scan.Rejected.Reason));
                }
            }

            totalRows += rows;
            sheets.Add(new SheetSummary(sheet.Name, rows, found, unique, duplicates, invalid));
        }

        return new ExtractionResult(
            occurrences,
            sheets,
            totalRows,
            NumbersFound: sheets.Sum(s => s.Found),
            UniqueNumbers: seen.Count,
            DuplicateNumbers: sheets.Sum(s => s.Duplicates),
            InvalidLines: sheets.Sum(s => s.Invalid));
    }
}
