using System.IO.Compression;
using YDot.Integration.Application.Abstractions.Files;

namespace YDot.Integration.Infrastructure.Files;

/// <summary>
/// Unpacks a CheckNumber.ai result archive defensively:
///
///   zip slip   an entry named "../../etc/x" or with an absolute path is refused, because every
///              target path must stay inside the destination folder;
///   zip bomb   too many entries, or more unpacked bytes than allowed, is refused - the byte count
///              is enforced WHILE copying, so a lying header cannot get past it.
/// </summary>
internal sealed class ZipArchiveExtractor : IArchiveExtractor
{
    public async Task<IReadOnlyList<ArchiveEntryInfo>> ExtractAsync(
        string zipPath, string destinationFolder, long maxTotalBytes, int maxEntries, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destinationFolder);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var entries = new List<ArchiveEntryInfo>();
        long total = 0;

        try
        {
            await using var archive = await ZipFile.OpenReadAsync(zipPath, cancellationToken);

            if (archive.Entries.Count > maxEntries)
            {
                throw new ArchiveExtractionException($"The archive has {archive.Entries.Count} entries; at most {maxEntries} are allowed.");
            }

            Directory.CreateDirectory(root);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) && target != root)
                {
                    throw new ArchiveExtractionException($"The archive entry '{entry.FullName}' points outside the extraction folder and was refused.");
                }

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                long written = 0;
                await using (var source = await entry.OpenAsync(cancellationToken))
                await using (var destination = File.Create(target))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        written += read;
                        total += read;
                        if (total > maxTotalBytes)
                        {
                            throw new ArchiveExtractionException($"The archive unpacks to more than {maxTotalBytes:N0} bytes and was refused.");
                        }

                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                entries.Add(new ArchiveEntryInfo(entry.FullName, written));
            }
        }
        catch (InvalidDataException exception)
        {
            throw new ArchiveExtractionException("The download is not a valid zip archive.", exception);
        }

        return entries;
    }
}
