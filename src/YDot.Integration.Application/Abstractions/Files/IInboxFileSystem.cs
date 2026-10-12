namespace YDot.Integration.Application.Abstractions.Files;

/// <summary>A data file waiting in the inbox.</summary>
/// <param name="FullPath">Absolute path of the data file.</param>
/// <param name="FileName">File name as it sits in the inbox ({upload-reference}.csv for web uploads).</param>
/// <param name="TenantFolder">Sub-folder name for inbox/{organisation}/file.txt; null in the inbox root.</param>
/// <param name="ManifestPath">The matching {name}.manifest.json, when there is one.</param>
public sealed record InboxCandidate(
    string FullPath,
    string FileName,
    string? TenantFolder,
    long SizeBytes,
    DateTimeOffset LastWriteUtc,
    string? ManifestPath);

/// <summary>
/// Written as claim.json into a claimed folder at the moment of the claim, so a folder whose
/// registration was interrupted (crash between the move and the database commit) still knows
/// everything needed to register it on the next run.
/// </summary>
public sealed record ClaimInfo(
    string OriginalFileName,
    string InboxFileName,
    string? TenantFolder,
    DateTimeOffset ClaimedAtUtc,
    string WorkerInstance);

/// <summary>A data file (and manifest) moved out of the inbox into its own folder.</summary>
/// <param name="Folder">files/yyyy/MM/{input-file-id}; the folder name IS the int_input_files id.</param>
public sealed record ClaimedFile(string DataFilePath, string? ManifestPath, string Folder, ClaimInfo Info);

/// <summary>
/// File-system operations on the inbox, behind an interface so the registration rules can be
/// read without System.IO noise.
/// </summary>
public interface IInboxFileSystem
{
    /// <summary>
    /// Data files with a supported extension (.txt, .csv, .xls, .xlsx) in the inbox root and one
    /// level of sub-folders. Temporary names (.tmp, .partial, .uploading, hidden files) are ignored,
    /// which is how a writer that renames into place is never read half-way.
    /// </summary>
    IReadOnlyList<InboxCandidate> ListCandidates();

    /// <summary>True when nobody else has the file open (it can be opened with no sharing).</summary>
    bool CanOpenExclusively(string path);

    /// <summary>
    /// Moves the data file (and its manifest) into <paramref name="destinationFolder"/> with an
    /// atomic rename and writes claim.json beside them. Returns null when the file is gone or
    /// locked - typically because another worker instance claimed it first, which is exactly the
    /// protection wanted.
    /// </summary>
    ClaimedFile? TryClaim(InboxCandidate candidate, string destinationFolder, ClaimInfo info);

    /// <summary>Moves a claimed file to the rejected area and writes reason.txt beside it; returns the new data file path.</summary>
    string MoveToRejected(ClaimedFile claimed, string rejectedFolder, string reason);

    /// <summary>Every claimed folder (files/yyyy/MM/{id}) that still holds its data file, for orphan recovery.</summary>
    IReadOnlyList<ClaimedFile> ListClaimedFolders();

    /// <summary>Lower-case hex SHA-256 of a file.</summary>
    Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken);
}
