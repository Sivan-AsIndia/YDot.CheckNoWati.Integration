using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Abstractions.Files;

/// <summary>One row of a sheet: its line number and its cells as text.</summary>
public sealed record FileRow(int LineNumber, IReadOnlyList<string> Cells);

/// <summary>One sheet ("Text file", "CSV file" or the Excel sheet name).</summary>
public sealed record FileSheet(string Name, IReadOnlyList<FileRow> Rows);

/// <summary>The whole file as text cells, sheet by sheet.</summary>
public sealed record NumberFileContents(FileKind Kind, IReadOnlyList<FileSheet> Sheets);

/// <summary>
/// Reads .txt, .csv, .xls and .xlsx files into rows of text cells, the same way the upload page
/// reads them in the browser (same line numbering, same CSV separator detection, numbers stored
/// as Excel numbers written out in full rather than as 9.87654E+09).
/// </summary>
public interface INumberFileReader
{
    Task<NumberFileContents> ReadAsync(string path, FileKind kind, CancellationToken cancellationToken);
}

/// <summary>The file could not be read (damaged, password-protected, wrong format).</summary>
public sealed class NumberFileReadException(string message, Exception? inner = null) : Exception(message, inner);
