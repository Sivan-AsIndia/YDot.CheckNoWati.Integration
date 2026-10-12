namespace YDot.Integration.Application.Abstractions.Files;

/// <summary>
/// The worker's folder layout, in one place:
///
///   {root}/inbox/                         new files arrive here (or in inbox/{organisation}/)
///   {root}/files/{yyyy}/{MM}/{file-id}/   the original file + its manifest, once claimed
///   {root}/checknumber/{job-number}/      submitted-numbers.txt, result.zip, extracted/
///   {root}/output/{job-number}/           whatsapp-results.csv, summary.json
///   {root}/rejected/{yyyy}/{MM}/{file-id}/ files that could not be used + reason.txt
///
/// Paths stored in the database are RELATIVE to the root, so the same rows stay valid when the
/// data folder moves (for example from a Windows folder to a Docker volume).
/// </summary>
public interface IDataPaths
{
    string Root { get; }

    string Inbox { get; }

    string FilesRoot { get; }

    string CheckNumberRoot { get; }

    string OutputRoot { get; }

    string RejectedRoot { get; }

    /// <summary>Creates every top-level folder (idempotent).</summary>
    void EnsureCreated();

    /// <summary>"files/2026/10/{id}" as an absolute path.</summary>
    string InputFileFolder(Guid inputFileId, DateTimeOffset receivedAtUtc);

    /// <summary>"checknumber/{job}" for the first task, "checknumber/{job}/task-02" for later ones.</summary>
    string CheckNumberFolder(string jobNumber, int taskSequence);

    string OutputFolder(string jobNumber);

    string RejectedFolder(Guid inputFileId, DateTimeOffset atUtc);

    /// <summary>Absolute path -> path relative to the root, with forward slashes.</summary>
    string ToRelative(string absolutePath);

    /// <summary>Relative path from the database -> absolute path on this machine.</summary>
    string ToAbsolute(string relativePath);
}
