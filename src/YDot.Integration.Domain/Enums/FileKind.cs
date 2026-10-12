namespace YDot.Integration.Domain.Enums;

/// <summary>The four file types the upload page accepts.</summary>
public enum FileKind
{
    /// <summary>Plain text, one number (or line of text) per line.</summary>
    Txt,

    /// <summary>Comma, semicolon, tab or pipe separated values.</summary>
    Csv,

    /// <summary>Excel 97-2003 workbook; every sheet is read.</summary>
    Xls,

    /// <summary>Excel workbook; every sheet is read.</summary>
    Xlsx
}
