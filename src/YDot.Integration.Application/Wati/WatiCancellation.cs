using Microsoft.EntityFrameworkCore;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Wati;

/// <summary>Stops WATI jobs: messages not yet handed to WATI are skipped, the rest finish normally.</summary>
public static class WatiCancellation
{
    public static async Task CancelQueuedAsync(
        IIntegrationDbContext db, IReadOnlyCollection<Guid> watiJobIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A conditional UPDATE: a message the dispatcher has already moved to Sending is left alone,
        // so a cancellation can never "un-send" a message that is on its way.
        await db.WatiMessages
            .Where(m => watiJobIds.Contains(m.WatiJobId) && m.Status == WatiMessageStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, WatiMessageStatus.Skipped)
                .SetProperty(m => m.SkipReason, SkipReasons.Cancelled)
                .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken);

        var jobs = await db.WatiJobs
            .Where(w => watiJobIds.Contains(w.Id))
            .ToListAsync(cancellationToken);

        foreach (var watiJob in jobs.Where(w => !w.IsTerminal))
        {
            var inFlight = await db.WatiMessages.AnyAsync(m => m.WatiJobId == watiJob.Id && m.Status == WatiMessageStatus.Sending, cancellationToken);
            if (inFlight)
            {
                continue; // the dispatcher finalises it once the batch in flight returns
            }

            await WatiJobCounters.RefreshAsync(db, watiJob, cancellationToken);
            watiJob.Status = watiJob.AcceptedCount > 0 || watiJob.FailedCount > 0
                ? WatiJobStatus.CompletedWithErrors
                : WatiJobStatus.Cancelled;
            watiJob.ErrorCode = ErrorCodes.CancelledByUser;
            watiJob.ErrorMessage = "Cancelled on request; messages not yet sent were skipped.";
            watiJob.CompletedAtUtc = now;
            watiJob.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Recomputes a WATI job's counters from its message rows (the rows are the truth).</summary>
public static class WatiJobCounters
{
    private static readonly WatiMessageStatus[] Accepted =
    [
        WatiMessageStatus.Pending,
        WatiMessageStatus.Sent,
        WatiMessageStatus.Delivered,
        WatiMessageStatus.Read,
        WatiMessageStatus.Replied,
    ];

    public static async Task RefreshAsync(IIntegrationDbContext db, Domain.Entities.WatiJob watiJob, CancellationToken cancellationToken)
    {
        var counts = await db.WatiMessages
            .Where(m => m.WatiJobId == watiJob.Id)
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(params WatiMessageStatus[] statuses) => counts.Where(c => statuses.Contains(c.Status)).Sum(c => c.Count);

        watiJob.TotalRecipients = counts.Sum(c => c.Count);
        watiJob.QueuedCount = Count(WatiMessageStatus.Queued, WatiMessageStatus.Sending);
        watiJob.AcceptedCount = Count(Accepted);
        watiJob.FailedCount = Count(WatiMessageStatus.Failed);
        watiJob.SkippedCount = Count(WatiMessageStatus.Skipped);
    }
}
