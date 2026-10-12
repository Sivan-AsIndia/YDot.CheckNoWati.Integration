using System.Globalization;
using System.Text;
using ExcelDataReader;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Infrastructure.Files;

/// <summary>
/// Reads number files into text cells exactly the way the upload page does in the browser
/// (UI/YDots.UI/.../WhatsApp/file-extract.ts), so the counts the review screen showed before the
/// upload are the counts the job reports afterwards:
///
///   .txt   one row per line (\r\n, \n or \r); line numbers are physical line numbers.
///   .csv   separator picked from , ; tab | by which splits the first 20 lines into the most
///          pieces; quoted fields and doubled quotes understood.
///   .xls / .xlsx   every sheet; blank rows dropped (the browser reads with blankrows:false, so
///          "line" is the position among the sheet's non-blank rows); a whole number stored as an
///          Excel number is written out in full (9876543210, never 9.87654E+09).
/// </summary>
internal sealed class NumberFileReader : INumberFileReader
{
    static NumberFileReader()
    {
        // .xls files use legacy code pages that .NET does not load by default.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<NumberFileContents> ReadAsync(string path, FileKind kind, CancellationToken cancellationToken)
    {
        try
        {
            return kind switch
            {
                FileKind.Txt => new NumberFileContents(kind, [new FileSheet("Text file", ToRows(await ReadLinesAsync(path, cancellationToken), line => [line]))]),
                FileKind.Csv => new NumberFileContents(kind, [new FileSheet("CSV file", ReadCsv(await ReadLinesAsync(path, cancellationToken)))]),
                _ => new NumberFileContents(kind, ReadWorkbook(path)),
            };
        }
        catch (NumberFileReadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new NumberFileReadException(
                $"'{Path.GetFileName(path)}' could not be read. It may be damaged or password-protected - open it, save a copy and upload that. ({exception.Message})",
                exception);
        }
    }

    private static async Task<string[]> ReadLinesAsync(string path, CancellationToken cancellationToken)
    {
        // UTF-8 (with or without BOM) is what the browser assumes; the BOM is dropped like file-extract.ts does.
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        return text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
    }

    private static List<FileRow> ToRows(IReadOnlyList<string> lines, Func<string, IReadOnlyList<string>> cells)
    {
        var rows = new List<FileRow>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            rows.Add(new FileRow(index + 1, cells(lines[index])));
        }

        return rows;
    }

    private static List<FileRow> ReadCsv(string[] lines)
    {
        var sample = string.Join('\n', lines.Take(20));
        var separator = new[] { ',', ';', '\t', '|' }
            .Select((s, order) => (Separator: s, Pieces: sample.Split(s).Length, Order: order))
            .OrderByDescending(x => x.Pieces)
            .ThenBy(x => x.Order)
            .First()
            .Separator;

        return ToRows(lines, line => ParseCsvLine(line, separator));
    }

    private static List<string> ParseCsvLine(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == separator)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }

    private static List<FileSheet> ReadWorkbook(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = new List<FileSheet>();

        do
        {
            var rows = new List<FileRow>();
            var position = 0;

            while (reader.Read())
            {
                var cells = new string[reader.FieldCount];
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    cells[column] = CellText(reader.GetValue(column));
                }

                if (cells.All(string.IsNullOrEmpty))
                {
                    continue;
                }

                rows.Add(new FileRow(++position, cells));
            }

            sheets.Add(new FileSheet(string.IsNullOrWhiteSpace(reader.Name) ? $"Sheet{sheets.Count + 1}" : reader.Name, rows));
        }
        while (reader.NextResult());

        if (sheets.Count == 0)
        {
            throw new NumberFileReadException($"'{Path.GetFileName(path)}' has no sheets.");
        }

        return sheets;
    }

    /// <summary>A cell as text, the way the upload page writes it.</summary>
    private static string CellText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        double number => Number(number),
        float number => Number(number),
        decimal number => Number((double)number),
        int or long or short or byte => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        bool flag => flag ? "true" : "false",
        DateTime date => Number(date.ToOADate()),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string Number(double number) =>
        Math.Abs(number % 1) < double.Epsilon && Math.Abs(number) < 1e17
            ? number.ToString("F0", CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);
}
