namespace YDot.Integration.Application.CheckNumber;

/// <summary>One row of a CheckNumber.ai result, as the service wrote it.</summary>
/// <param name="Number">Digits without "+", e.g. 919841465656.</param>
/// <param name="Activated">"yes" = registered on WhatsApp, "no" = not registered.</param>
public sealed record ResultRow(string Number, string Activated);

/// <summary>The rows and which file they came from.</summary>
public sealed record ResultRows(IReadOnlyList<ResultRow> Rows, string Source);

/// <summary>
/// Reads an extracted CheckNumber.ai result archive. Inspected format (Phase3 runs and the live
/// task dakhvuup2jvs5qlkmpq0): the zip holds a folder named after the task_id with
///
///   all.csv            header "number,activated", one row per number, activated = yes | no
///   activated.txt      the numbers that have WhatsApp, one per line
///   unregistered.txt   the numbers that do not
///   result.xlsx        the same data for people
///
/// all.csv is preferred; the two text files are the fallback when it is absent.
/// </summary>
public static class CheckNumberResultReader
{
    public static ResultRows Read(string extractedFolder)
    {
        if (FindFile(extractedFolder, "all.csv") is { } allCsv)
        {
            return new ResultRows(ReadAllCsv(allCsv), "all.csv");
        }

        var activated = FindFile(extractedFolder, "activated.txt");
        var unregistered = FindFile(extractedFolder, "unregistered.txt");

        if (activated is null && unregistered is null)
        {
            throw new InvalidDataException(
                "The CheckNumber.ai result archive contains neither all.csv nor activated.txt / unregistered.txt.");
        }

        return new ResultRows(
            [.. ReadNumberList(activated, "yes"), .. ReadNumberList(unregistered, "no")],
            "activated.txt+unregistered.txt");
    }

    private static List<ResultRow> ReadAllCsv(string path)
    {
        using var lines = File.ReadLines(path).GetEnumerator();

        if (!lines.MoveNext())
        {
            throw new InvalidDataException("all.csv in the CheckNumber.ai result is empty.");
        }

        var header = SplitCsvLine(lines.Current.TrimStart('﻿'));
        var numberColumn = Array.FindIndex(header, c => c.Equals("number", StringComparison.OrdinalIgnoreCase));
        var activatedColumn = Array.FindIndex(header, c => c.Equals("activated", StringComparison.OrdinalIgnoreCase));

        if (numberColumn < 0 || activatedColumn < 0)
        {
            throw new InvalidDataException(
                $"all.csv needs 'number' and 'activated' columns but its header is '{lines.Current}'.");
        }

        var rows = new List<ResultRow>();
        while (lines.MoveNext())
        {
            var cells = SplitCsvLine(lines.Current);
            if (cells.Length > Math.Max(numberColumn, activatedColumn) && cells[numberColumn].Length > 0)
            {
                rows.Add(new ResultRow(cells[numberColumn], cells[activatedColumn]));
            }
        }

        return rows;
    }

    private static IEnumerable<ResultRow> ReadNumberList(string? path, string activated) =>
        path is null
            ? []
            : File.ReadLines(path)
                .Select(line => line.Trim().TrimStart('﻿'))
                .Where(line => line.Length > 0)
                .Select(line => new ResultRow(line, activated));

    private static string? FindFile(string folder, string fileName) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, fileName, SearchOption.AllDirectories).FirstOrDefault()
            : null;

    private static string[] SplitCsvLine(string line) =>
        line.Split(',').Select(cell => cell.Trim().Trim('"')).ToArray();
}
