using System.Collections.Concurrent;
using YDot.Integration.Application.Abstractions.Files;

namespace YDot.Integration.Application.Inbox;

/// <summary>
/// Decides when a file copied into the inbox is complete.
///
/// A file being copied in (Explorer, an SFTP upload, a network share) appears with its final name
/// while bytes are still arriving. So a file is only treated as ready once its size and
/// modification time have stayed the same for Worker:FileStabilitySeconds across scans - and the
/// caller then also checks that it can be opened with no sharing, which fails while any writer
/// still has it open.
///
/// Web uploads skip the stability wait: the CAM API renames a finished file into place (and wrote
/// its manifest before), so a file with a manifest is complete the moment it is visible.
/// </summary>
public sealed class InboxReadinessTracker(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, (long Size, DateTimeOffset LastWrite, DateTimeOffset FirstSeen)> observations =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsReady(InboxCandidate candidate, TimeSpan stability)
    {
        if (candidate.ManifestPath is not null)
        {
            return true;
        }

        var now = time.GetUtcNow();
        var current = (candidate.SizeBytes, candidate.LastWriteUtc);

        if (!observations.TryGetValue(candidate.FullPath, out var seen)
            || seen.Size != current.SizeBytes
            || seen.LastWrite != current.LastWriteUtc)
        {
            observations[candidate.FullPath] = (current.SizeBytes, current.LastWriteUtc, now);
            return stability <= TimeSpan.Zero && candidate.SizeBytes > 0;
        }

        return now - seen.FirstSeen >= stability;
    }

    /// <summary>Drops what is known about a file that has been claimed or has disappeared.</summary>
    public void Forget(string fullPath) => observations.TryRemove(fullPath, out _);

    /// <summary>Forgets files that are no longer in the inbox, so the dictionary cannot grow forever.</summary>
    public void Prune(IEnumerable<InboxCandidate> present)
    {
        var keep = present.Select(c => c.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in observations.Keys.Where(path => !keep.Contains(path)).ToList())
        {
            observations.TryRemove(path, out _);
        }
    }
}
