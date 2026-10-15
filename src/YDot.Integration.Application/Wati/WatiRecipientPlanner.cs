using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.PhoneNumbers;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Wati;

/// <summary>A number that might receive a message.</summary>
/// <param name="SourceJobId">The check job whose verified answer made the number eligible.</param>
/// <param name="SourceJobNumber">That job's number, for the audit columns.</param>
/// <param name="ContactName">The name the source file gave beside the number.</param>
/// <param name="Parameters">This recipient's template values, when they differ from the job's (bound parameters).</param>
public sealed record WatiCandidate(
    Guid PhoneNumberId,
    string Digits,
    WhatsAppStatus WhatsAppStatus,
    Guid? CheckNumberTaskId,
    string? CheckNumberProviderTaskId,
    Guid? SourceJobId = null,
    string? SourceJobNumber = null,
    string? ContactName = null,
    IReadOnlyList<TemplateParameter>? Parameters = null);

/// <summary>The decision for one candidate.</summary>
public sealed record PlannedMessage(WatiCandidate Candidate, WatiMessageStatus Status, string? SkipReason);

/// <summary>
/// Decides who gets a message. The rules, applied in this order, each with the reason recorded
/// on the skipped message:
///
///   1. not on WhatsApp                       -> WHATSAPP_NOT_AVAILABLE
///   2. CheckNumber.ai could not tell         -> WHATSAPP_UNVERIFIED  (unless SendToUnverifiedNumbers)
///   3. test allow-list set, number not on it  -> NOT_IN_ALLOWLIST
///   4. same template already sent to this number for the same campaign -> ALREADY_SENT
///   5. Wati:MaxRecipientsPerJob reached        -> MAX_RECIPIENTS_REACHED
///
/// A number listed twice is planned once. Every send - a person's selection, or an older automatic
/// send still in flight - goes through the same rules, so nobody on the Wati page can bypass the
/// safety settings. The candidates are only ever the numbers the person selected: the planner can
/// leave one out, never add one.
/// </summary>
public sealed class WatiRecipientPlanner(IIntegrationDbContext db, IOptions<WatiOptions> options, TimeProvider time)
{
    private static readonly WatiMessageStatus[] AcceptedStatuses =
    [
        WatiMessageStatus.Pending,
        WatiMessageStatus.Sent,
        WatiMessageStatus.Delivered,
        WatiMessageStatus.Read,
        WatiMessageStatus.Replied,
    ];

    public async Task<IReadOnlyList<PlannedMessage>> PlanAsync(
        Guid tenantId,
        Guid? campaignId,
        string templateName,
        IReadOnlyList<WatiCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var allowList = settings.AllowedNumbers
            .Select(PhoneNumberNormalizer.DigitsOnly)
            .Where(digits => digits.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var alreadySent = new HashSet<Guid>();
        if (settings.SkipAlreadyMessagedNumbers && candidates.Count > 0)
        {
            var ids = candidates.Select(c => c.PhoneNumberId).Distinct().ToList();
            DateTimeOffset? since = settings.DuplicateWindowDays > 0
                ? time.GetUtcNow().AddDays(-settings.DuplicateWindowDays)
                : null;

            var sent = await db.WatiMessages
                .Where(m => m.TenantId == tenantId
                            && m.TemplateName == templateName
                            && m.CampaignId == campaignId
                            && ids.Contains(m.PhoneNumberId)
                            && AcceptedStatuses.Contains(m.Status)
                            && (since == null || m.CompletedAtUtc >= since))
                .Select(m => m.PhoneNumberId)
                .Distinct()
                .ToListAsync(cancellationToken);

            alreadySent = [.. sent];
        }

        var planned = new List<PlannedMessage>(candidates.Count);
        var seen = new HashSet<Guid>();
        var queued = 0;

        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate.PhoneNumberId))
            {
                continue;
            }

            string? reason = candidate.WhatsAppStatus switch
            {
                WhatsAppStatus.No => SkipReasons.WhatsAppNotAvailable,
                WhatsAppStatus.Unknown when !settings.SendToUnverifiedNumbers => SkipReasons.WhatsAppUnverified,
                _ => null,
            };

            if (reason is null && allowList.Count > 0 && !allowList.Contains(candidate.Digits))
            {
                reason = SkipReasons.NotInAllowList;
            }

            if (reason is null && alreadySent.Contains(candidate.PhoneNumberId))
            {
                reason = SkipReasons.AlreadySent;
            }

            if (reason is null && settings.MaxRecipientsPerJob > 0 && queued >= settings.MaxRecipientsPerJob)
            {
                reason = SkipReasons.MaxRecipientsReached;
            }

            if (reason is null)
            {
                queued++;
                planned.Add(new PlannedMessage(candidate, WatiMessageStatus.Queued, null));
            }
            else
            {
                planned.Add(new PlannedMessage(candidate, WatiMessageStatus.Skipped, reason));
            }
        }

        return planned;
    }
}
