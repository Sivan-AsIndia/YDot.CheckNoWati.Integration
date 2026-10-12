using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Wati;

/// <summary>
/// Turns WATI's statusString (PENDING, SENT, DELIVERED, READ, REPLIED, FAILED) into a message
/// status, and applies it to a message without ever moving it backwards (a message that was read
/// is never "un-read" by a late answer).
/// </summary>
public static class WatiStatusMapping
{
    public static WatiMessageStatus? Map(string? statusString) =>
        statusString?.Trim().ToUpperInvariant() switch
        {
            "PENDING" or "QUEUED" or "SCHEDULED" or "PROCESSING" => WatiMessageStatus.Pending,
            "SENT" => WatiMessageStatus.Sent,
            "DELIVERED" => WatiMessageStatus.Delivered,
            "READ" => WatiMessageStatus.Read,
            "REPLIED" => WatiMessageStatus.Replied,
            "FAILED" or "ERROR" or "UNDELIVERED" or "REJECTED" => WatiMessageStatus.Failed,
            _ => null,
        };

    private static int Rank(WatiMessageStatus status) => status switch
    {
        WatiMessageStatus.Pending => 1,
        WatiMessageStatus.Sent => 2,
        WatiMessageStatus.Delivered => 3,
        WatiMessageStatus.Read => 4,
        WatiMessageStatus.Replied => 5,
        WatiMessageStatus.Failed => 6,
        _ => 0,
    };

    /// <summary>Applies a lookup to a message; returns true when anything changed.</summary>
    public static bool Apply(WatiMessage message, WatiMessageLookup lookup, DateTimeOffset observedAtUtc)
    {
        message.DeliveryStatusRaw = lookup.StatusString ?? message.DeliveryStatusRaw;
        message.WatiMessageId ??= lookup.WatiMessageId;
        message.WhatsAppMessageId ??= lookup.WhatsAppMessageId;
        message.ConversationId ??= lookup.ConversationId;

        // Queued / Sending rank 0, so any WATI status moves them on; nothing ever moves backwards.
        if (Map(lookup.StatusString) is not { } next || Rank(next) <= Rank(message.Status))
        {
            return false;
        }

        // WATI'S ONE TIMESTAMP IS WHEN THE MESSAGE WAS CREATED: its lookup carries no time for the later
        // stages. So that time dates "sent" only, and delivered, read, replied and failed are dated by
        // when the worker saw the change - within one check interval of the real moment, and flagged
        // (StatusTimesObserved). Using the creation time for every stage made a message look read the
        // very second it was sent (seen on 10 Oct 2026).
        var sentAt = lookup.Timestamp ?? observedAtUtc;
        var observed = lookup.Timestamp is null;

        DateTimeOffset Observed()
        {
            observed = true;
            return observedAtUtc;
        }

        // Reaching a later stage implies the earlier ones happened (a read message was delivered and sent).
        if (next is WatiMessageStatus.Sent or WatiMessageStatus.Delivered or WatiMessageStatus.Read or WatiMessageStatus.Replied)
        {
            message.SentAtUtc ??= sentAt;
        }

        if (next is WatiMessageStatus.Delivered or WatiMessageStatus.Read or WatiMessageStatus.Replied)
        {
            message.DeliveredAtUtc ??= Observed();
        }

        if (next is WatiMessageStatus.Read or WatiMessageStatus.Replied)
        {
            message.ReadAtUtc ??= Observed();
        }

        if (next == WatiMessageStatus.Replied)
        {
            message.RepliedAtUtc ??= Observed();
        }

        if (next == WatiMessageStatus.Failed)
        {
            message.FailedAtUtc ??= Observed();
            message.FailedCode = lookup.FailedCode ?? message.FailedCode ?? "FAILED";
            message.FailedReason = string.IsNullOrWhiteSpace(lookup.FailedDetail) ? message.FailedReason : lookup.FailedDetail;
        }

        if (observed)
        {
            message.StatusTimesObserved = true;
        }

        message.Status = next;
        return true;
    }
}
