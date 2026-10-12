using System.Text.RegularExpressions;

namespace YDot.Integration.Application.PhoneNumbers;

/// <summary>A number found in a row: the E.164 result and the text it came from.</summary>
public sealed record FoundNumber(string E164, string Raw);

/// <summary>Why a row gave no number.</summary>
public sealed record RejectedRow(string Raw, string Reason);

/// <summary>Everything one row held.</summary>
public sealed record RowScan(IReadOnlyList<FoundNumber> Numbers, RejectedRow? Rejected);

/// <summary>
/// Finds the phone numbers in one row (a text line, or the cells of a spreadsheet row) with the
/// rules of the upload page (number-tools.ts, scanRow):
///
///   - letters are ignored, only digit runs (with spaces, dots, dashes, brackets) are considered,
///     so "Ravi 98765 43210" and "ID-44, 9876543210" both yield +919876543210;
///   - several whole numbers on one line separated by spaces are read one by one;
///   - a row that yields at least one valid number is good - stray short digits next to it are
///     not reported;
///   - a row that yields none is reported ONCE, with the reason for its first candidate.
/// </summary>
public static partial class RowScanner
{
    public const string NoPhoneNumberReason = "No phone number found";

    public static RowScan Scan(IReadOnlyList<string> cells)
    {
        var numbers = new List<FoundNumber>();
        RejectedRow? firstReject = null;
        var sawDigits = false;

        foreach (var cell in cells)
        {
            var text = cell ?? string.Empty;
            if (!text.Any(char.IsAsciiDigit))
            {
                continue;
            }

            sawDigits = true;
            var anyCandidate = false;

            foreach (Match match in Candidate().Matches(text))
            {
                anyCandidate = true;
                var value = match.Value;
                var tokens = Whitespace().Split(value.Trim());

                // Two or more whole numbers on one line, separated by spaces: read each on its own.
                if (tokens.Length > 1
                    && PhoneNumberNormalizer.DigitsOnly(value).Length > 15
                    && tokens.All(token => PhoneNumberNormalizer.DigitsOnly(token).Length >= 10))
                {
                    foreach (var token in tokens)
                    {
                        var part = PhoneNumberNormalizer.Normalise(token, token.StartsWith('+'));
                        if (part.Ok)
                        {
                            numbers.Add(new FoundNumber(part.E164!, token));
                        }
                        else
                        {
                            firstReject ??= new RejectedRow(token, part.Reason!);
                        }
                    }

                    continue;
                }

                var result = PhoneNumberNormalizer.Normalise(value, match.Groups[1].Value == "+");
                if (result.Ok)
                {
                    numbers.Add(new FoundNumber(result.E164!, value.Trim()));
                }
                else
                {
                    firstReject ??= new RejectedRow(value.Trim(), result.Reason!);
                }
            }

            if (!anyCandidate && firstReject is null)
            {
                var digits = PhoneNumberNormalizer.DigitsOnly(text);
                firstReject = new RejectedRow(text.Trim(), $"Too short ({digits.Length} digits)");
            }
        }

        if (numbers.Count > 0)
        {
            return new RowScan(numbers, null);
        }

        var rawRow = string.Join(", ", cells.Select(c => (c ?? string.Empty).Trim()).Where(c => c.Length > 0));

        if (firstReject is not null)
        {
            return new RowScan(numbers, new RejectedRow(rawRow.Length > 0 ? rawRow : firstReject.Raw, firstReject.Reason));
        }

        if (!sawDigits && rawRow.Length > 0)
        {
            return new RowScan(numbers, new RejectedRow(rawRow, NoPhoneNumberReason));
        }

        return new RowScan(numbers, null);
    }

    /// <summary>A run of digits that may be separated by spaces, dots, dashes or brackets, with an optional "+".</summary>
    [GeneratedRegex(@"(\+?)\s*\(?[0-9][0-9\s().\-]{6,}[0-9]")]
    private static partial Regex Candidate();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
