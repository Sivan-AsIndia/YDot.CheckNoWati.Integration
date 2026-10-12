using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Common;

namespace YDot.Integration.Infrastructure.Files;

/// <summary>System.IO side of the inbox (see <see cref="IInboxFileSystem"/>).</summary>
internal sealed class InboxFileSystem(IDataPaths paths, ILogger<InboxFileSystem> logger) : IInboxFileSystem
{
    private const string ManifestSuffix = ".manifest.json";
    private const string ClaimFileName = "claim.json";
    private const string ClaimedManifestName = "manifest.json";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".xls", ".xlsx" };

    private static readonly string[] TemporarySuffixes = [".tmp", ".partial", ".uploading", ".crdownload", ".part"];

    public IReadOnlyList<InboxCandidate> ListCandidates()
    {
        if (!Directory.Exists(paths.Inbox))
        {
            return [];
        }

        var candidates = new List<InboxCandidate>();
        Collect(paths.Inbox, tenantFolder: null, candidates);

        foreach (var folder in Directory.EnumerateDirectories(paths.Inbox))
        {
            var name = Path.GetFileName(folder);
            if (!name.StartsWith('.') && !name.StartsWith('_'))
            {
                Collect(folder, name, candidates);
            }
        }

        return candidates;
    }

    private static void Collect(string folder, string? tenantFolder, List<InboxCandidate> candidates)
    {
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.StartsWith("~$", StringComparison.Ordinal)
                || TemporarySuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                || !SupportedExtensions.Contains(Path.GetExtension(name)))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (IOException)
            {
                continue;
            }

            var manifest = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + ManifestSuffix);
            candidates.Add(new InboxCandidate(
                path, name, tenantFolder, info.Length, info.LastWriteTimeUtc, File.Exists(manifest) ? manifest : null));
        }
    }

    public bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public ClaimedFile? TryClaim(InboxCandidate candidate, string destinationFolder, ClaimInfo info)
    {
        Directory.CreateDirectory(destinationFolder);
        var dataPath = Path.Combine(destinationFolder, info.OriginalFileName);

        try
        {
            // The data file moves FIRST. If that fails nothing has changed; if the manifest moved first
            // and the data move then failed, the file would be left behind looking like a folder drop.
            File.Move(candidate.FullPath, dataPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug("Could not claim {File}: {Reason}", candidate.FileName, exception.Message);
            TryDeleteEmptyFolder(destinationFolder);
            return null;
        }

        string? manifestPath = null;
        if (candidate.ManifestPath is not null)
        {
            manifestPath = Path.Combine(destinationFolder, ClaimedManifestName);
            try
            {
                File.Move(candidate.ManifestPath, manifestPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep a copy; the orphaned original in the inbox is harmless (it has no data file).
                logger.LogWarning("Could not move the manifest of {File} ({Reason}); a copy is used.", candidate.FileName, exception.Message);
                File.Copy(candidate.ManifestPath, manifestPath, overwrite: true);
            }
        }

        File.WriteAllText(Path.Combine(destinationFolder, ClaimFileName), JsonSerializer.Serialize(info, JsonDefaults.Indented));
        return new ClaimedFile(dataPath, manifestPath, destinationFolder, info);
    }

    public string MoveToRejected(ClaimedFile claimed, string rejectedFolder, string reason)
    {
        Directory.CreateDirectory(rejectedFolder);
        var dataPath = Path.Combine(rejectedFolder, Path.GetFileName(claimed.DataFilePath));

        MoveIfExists(claimed.DataFilePath, dataPath);
        if (claimed.ManifestPath is not null)
        {
            MoveIfExists(claimed.ManifestPath, Path.Combine(rejectedFolder, ClaimedManifestName));
        }

        MoveIfExists(Path.Combine(claimed.Folder, ClaimFileName), Path.Combine(rejectedFolder, ClaimFileName));
        File.WriteAllText(Path.Combine(rejectedFolder, "reason.txt"), reason);
        TryDeleteEmptyFolder(claimed.Folder);
        return dataPath;
    }

    public IReadOnlyList<ClaimedFile> ListClaimedFolders()
    {
        var claimed = new List<ClaimedFile>();
        if (!Directory.Exists(paths.FilesRoot))
        {
            return claimed;
        }

        foreach (var claimFile in Directory.EnumerateFiles(paths.FilesRoot, ClaimFileName, SearchOption.AllDirectories))
        {
            try
            {
                var info = JsonSerializer.Deserialize<ClaimInfo>(File.ReadAllText(claimFile), JsonDefaults.Options);
                var folder = Path.GetDirectoryName(claimFile)!;
                if (info is null)
                {
                    continue;
                }

                var dataPath = Path.Combine(folder, info.OriginalFileName);
                if (!File.Exists(dataPath))
                {
                    continue;
                }

                var manifest = Path.Combine(folder, ClaimedManifestName);
                claimed.Add(new ClaimedFile(dataPath, File.Exists(manifest) ? manifest : null, folder, info));
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                logger.LogWarning("Could not read {ClaimFile}: {Reason}", claimFile, exception.Message);
            }
        }

        return claimed;
    }

    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static void MoveIfExists(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite: true);
        }
    }

    private static void TryDeleteEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (IOException)
        {
            // Not important: an empty folder is harmless.
        }
    }
}
