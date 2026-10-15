using System.Text.RegularExpressions;

namespace YDot.Integration.Application.PhoneNumbers;

/// <summary>
/// Finds the person's name in a row that holds a phone number, so a template can greet them by it
/// ({{name}}) when the number is later picked for a send.
///
/// BEST EFFORT, AND CAUTIOUS ABOUT IT. A wrong name in "Dear {{name}}" is worse than none - the
/// send falls back to the value the person typed ("Friend") when there is no name - so a cell is
/// only taken as a name where a list would normally put one:
///
///   1. the sheet has a heading row ("Name, Phone"): the column(s) headed as a name - "Name",
///      "Full name", "Donor name", or "First name" + "Last name" joined - and nothing else, so a
///      "City" column is never mistaken for a person;
///   2. no heading row: the first cell of the row with letters and no digits ("Ravi Kumar,
///      Chennai, 98765 43210" gives "Ravi Kumar");
///   3. otherwise the words around the number in its own cell ("Ravi 98765-43210" gives "Ravi"),
///      with labels such as "Mob:" or "Ph." left out.
///
/// E-mail addresses, links and placeholders ("n/a", "-") are never names.
/// </summary>
public static partial class ContactNameReader
{
    /// <summary>The longest name kept (int_input_file_numbers.contact_name).</summary>
    public const int MaxLength = 120;

    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "na", "n/a", "nil", "null", "none", "unknown", "-", "--", "---", ".", "?", "x", "tbd",
    };

    /// <summary>Words that label a number rather than name a person ("Mob: 98765 43210").</summary>
    private static readonly HashSet<string> NumberLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "ph", "phone", "mob", "mobile", "tel", "telephone", "cell", "whatsapp", "wa", "no", "num",
        "number", "contact", "id", "call", "landline", "alt", "alternate",
    };

    /// <summary>Headings that name something other than a person, even though they say "name".</summary>
    private static readonly string[] NotAPersonHeading =
    [
        "file", "user", "company", "organisation", "organization", "campaign", "city", "town",
        "village", "street", "place", "district", "state", "country", "branch", "school", "college",
        "bank", "business", "brand", "product", "event", "group", "team", "sheet", "list",
    ];

    /// <summary>
    /// The columns of a heading row that hold a person's name, in column order; null when the row
    /// is not a heading (it has digits) or names no such column.
    /// </summary>
    public static IReadOnlyList<int>? NameColumns(IReadOnlyList<string> headingCells)
    {
        if (headingCells.Count == 0 || headingCells.Any(cell => (cell ?? string.Empty).Any(char.IsAsciiDigit)))
        {
            return null;
        }

        var whole = new List<int>();
        var parts = new List<int>();

        for (var i = 0; i < headingCells.Count; i++)
        {
            var heading = Spaces().Replace((headingCells[i] ?? string.Empty).Trim().ToLowerInvariant(), " ");
            if (!NameWord().IsMatch(heading) || NotAPersonHeading.Any(word => heading.Contains(word, StringComparison.Ordinal)))
            {
                continue;
            }

            // "First name", "Middle name", "Last name", "Surname" are pieces of one name; any other
            // "... name" heading ("Name", "Full name", "Donor name") holds the whole of it.
            if (PartOfName().IsMatch(heading))
            {
                parts.Add(i);
            }
            else
            {
                whole.Add(i);
            }
        }

        if (whole.Count > 0)
        {
            return [whole[0]];
        }

        return parts.Count > 0 ? parts : null;
    }

    /// <summary>True when the row is a heading row: it has text and no digit at all.</summary>
    public static bool IsHeading(IReadOnlyList<string> cells) =>
        cells.Any(cell => !string.IsNullOrWhiteSpace(cell))
        && !cells.Any(cell => (cell ?? string.Empty).Any(char.IsAsciiDigit));

    /// <summary>
    /// The name in one row of a sheet. <paramref name="nameColumns"/> comes from the sheet's
    /// heading row (rule 1); <paramref name="sheetHasHeading"/> is true when the sheet has a heading
    /// row at all - then only rule 3 may add to it, because the heading said what each column holds.
    /// </summary>
    public static string? FromRow(IReadOnlyList<string> cells, IReadOnlyList<int>? nameColumns, bool sheetHasHeading)
    {
        if (nameColumns is { Count: > 0 })
        {
            var joined = string.Join(' ', nameColumns
                .Where(index => index < cells.Count)
                .Select(index => Clean(cells[index]))
                .Where(part => part is not null));

            return Limit(joined);
        }

        if (!sheetHasHeading)
        {
            foreach (var cell in cells)
            {
                var text = cell ?? string.Empty;
                if (text.Any(char.IsLetter) && !text.Any(char.IsAsciiDigit) && Clean(text) is { } name)
                {
                    return Limit(name);
                }
            }
        }

        foreach (var cell in cells)
        {
            var text = cell ?? string.Empty;
            if (!text.Any(char.IsLetter) || !text.Any(char.IsAsciiDigit))
            {
                continue;
            }

            var words = DigitRun().Replace(text, " ")
                .Split([' ', ',', ';', ':', '|', '/', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Trim('.', '-', '(', ')', '#', '*', '"', '\''))
                .Where(word => word.Length > 0 && !NumberLabels.Contains(word));

            if (Clean(string.Join(' ', words)) is { } name)
            {
                return Limit(name);
            }
        }

        return null;
    }

    /// <summary>The cell as a name: trimmed and tidied, or null when it cannot be one.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Spaces().Replace(value.Trim(), " ").Trim(' ', ',', ';', ':', '-', '|', '#', '*', '"', '\'', '(', ')');

        if (text.Length < 2
            || !text.Any(char.IsLetter)
            || Placeholders.Contains(text)
            || text.Contains('@')
            || text.Contains("http", StringComparison.OrdinalIgnoreCase)
            || text.Contains("www.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return text;
    }

    /// <summary>Cuts a long value at a word boundary within <see cref="MaxLength"/>.</summary>
    private static string? Limit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length <= MaxLength)
        {
            return value;
        }

        var cut = value[..MaxLength];
        var space = cut.LastIndexOf(' ');
        return (space > MaxLength / 2 ? cut[..space] : cut).TrimEnd();
    }

    [GeneratedRegex(@"\bname\b|\bsurname\b")]
    private static partial Regex NameWord();

    [GeneratedRegex(@"\b(first|middle|last|given|family|sur)\s?name\b|\bsurname\b")]
    private static partial Regex PartOfName();

    /// <summary>A phone number or any other run of digits, with its spaces, dots, dashes and brackets.</summary>
    [GeneratedRegex(@"\+?\(?[0-9][0-9\s().\-]*[0-9]|[0-9]")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
