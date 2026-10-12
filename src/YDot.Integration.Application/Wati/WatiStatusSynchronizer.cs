using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Options;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Wati;

/// <summary>
/// Follows each accepted message to Sent, Delivered, Read (or Failed) by asking WATI about its
/// localMessageId - GET /{tenant}/api/v1/whatsApp/messages/{channel}/{localMessageId}.
///
/// Checks are spread out: the first a few seconds after WATI accepted the message, then at a
/// widening interval (CheckIntervalSeconds, doubling, capped at MaxCheckIntervalMinutes), and
/// never after MaxTrackingHours. A message that reached Read, Replied or Failed is not checked
/// again.
///
/// WHY POLLING AND NOT WEBHOOKS. The worker has no public HTTP endpoint for WATI to call. Webhooks
/// would give exact times and replies (sentMessageREPLIED_v2); the README explains how to add them.
/// Times recorded by polling are the moment the change was OBSERVED (StatusTimesObserved = true).
/// </summary>
public sealed class WatiStatusSynchronizer(
    IIntegrationDbContext db,
    IWatiClient client,
    IOptions<WatiOptions> options,
    TimeProvider time,
    ILogger<WatiStatusSynchronizer> logger)
{
    private static readonly WatiMessageStatus[] Followed =
    [
        WatiMessageStatus.Pending,
        WatiMessageStatus.Sent,
        WatiMessageStatus.Delivered,
    ];

    /// <summary>Checks one round of due messages; returns how many were checked.</summary>
    public async Task<int> SyncDueAsync(CancellationToken cancellationToken)
    {
        if (!client.IsConfigured)
        {
            return 0;
        }

        var settings = options.Value.StatusSync;
        var now = time.GetUtcNow();
        var trackingStart = now.AddHours(-settings.MaxTrackingHours);

        // Messages that aged out of the tracking window stop being followed.
        await db.WatiMessages
            .Where(m => Followed.Contains(m.Status) && m.NextStatusCheckAtUtc != null && m.QueuedAtUtc < trackingStart)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextStatusCheckAtUtc, (DateTimeOffset?)null), cancellationToken);

        var due = await db.WatiMessages
            .Where(m => Followed.Contains(m.Status)
                        && m.NextStatusCheckAtUtc != null
                        && m.NextStatusCheckAtUtc <= now)
            .OrderBy(m => m.NextStatusCheckAtUtc)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        var changed = 0;
        foreach (var message in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new ApiCallContext
            {
                TenantId = message.TenantId,
                JobId = message.IntegrationJobId,
                WatiJobId = message.WatiJobId,
                RelatedEntityId = message.Id,
                StepCode = StepCodes.WatiStatusSync,
            };

            try
            {
                var lookup = await client.GetMessageAsync(message.LocalMessageId, context, cancellationToken);
                if (lookup.Found && WatiStatusMapping.Apply(message, lookup, time.GetUtcNow()))
                {
                    changed++;
                }
            }
            catch (ExternalApiException exception) when (exception.IsAuthenticationFailure)
            {
                // A bad token fails every check the same way: stop this round and wait for the next.
                logger.LogWarning("WATI refused the status check (HTTP {Status}); message tracking pauses until the token is fixed.", exception.StatusCode);
                await BackOffAsync(due, settings, cancellationToken);
                return 0;
            }
            catch (ExternalApiException exception)
            {
                logger.LogDebug("Status check for {LocalMessageId} failed: {Reason}", message.LocalMessageId, exception.Message);
            }

            var checkedAt = time.GetUtcNow();
            message.StatusCheckCount++;
            message.LastStatusCheckAtUtc = checkedAt;
            message.UpdatedAtUtc = checkedAt;
            message.NextStatusCheckAtUtc = message.IsDeliveryFinal
                ? null
                : checkedAt.Add(NextInterval(message.StatusCheckCount, settings));
        }

        await db.SaveChangesAsync(cancellationToken);

        if (changed > 0)
        {
            logger.LogInformation("WATI status sync: {Checked} message(s) checked, {Changed} changed status.", due.Count, changed);
        }

        return due.Count;
    }

    private async Task BackOffAsync(IEnumerable<Domain.Entities.WatiMessage> messages, WatiStatusSyncOptions settings, CancellationToken cancellationToken)
    {
        var later = time.GetUtcNow().AddMinutes(settings.MaxCheckIntervalMinutes);
        foreach (var message in messages)
        {
            message.NextStatusCheckAtUtc = later;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static TimeSpan NextInterval(int checksDone, WatiStatusSyncOptions settings)
    {
        var seconds = settings.CheckIntervalSeconds * Math.Pow(2, Math.Min(checksDone - 1, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, settings.MaxCheckIntervalMinutes * 60.0));
    }
}
