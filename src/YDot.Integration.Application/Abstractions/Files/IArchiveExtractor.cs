namespace YDot.Integration.Application.Abstractions.Files;

/// <summary>One file found in an archive.</summary>
public sealed record ArchiveEntryInfo(string Name, long SizeBytes);

/// <summary>
/// Unpacks a zip archive SAFELY: entries that would land outside the destination folder
/// ("zip slip") are refused, and an archive with too many entries or too many bytes once
/// unpacked ("zip bomb") is refused before it fills the disk.
/// </summary>
public interface IArchiveExtractor
{
    Task<IReadOnlyList<ArchiveEntryInfo>> ExtractAsync(
        string zipPath, string destinationFolder, long maxTotalBytes, int maxEntries, CancellationToken cancellationToken);
}

/// <summary>The archive is not a valid zip, or breaks one of the safety limits.</summary>
public sealed class ArchiveExtractionException(string message, Exception? inner = null) : Exception(message, inner);
