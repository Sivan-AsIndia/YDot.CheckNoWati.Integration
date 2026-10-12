using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Commands;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.PhoneNumbers;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Wati;

/// <summary>
/// Creates WATI jobs and their message rows - automatically for a finished check, or from a
/// manual broadcast request. Every verified number of the job gets a message row: Queued when it
/// will be sent, Skipped with the reason when it will not.
/// </summary>
public sealed class WatiJobFactory(
    IIntegrationDbContext db,
    WatiRecipientPlanner planner,
    IReferenceGenerator references,
    IJobJournal journal,
    IOptions<WatiOptions> options,
    TimeProvider time,
    ILogger<WatiJobFactory> logger)
{
    /// <summary>The WATI job for an integration job: every distinct number of the file is a candidate.</summary>
    public async Task<WatiJob> CreateForIntegrationJobAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var templateName = FirstNonBlank(job.WatiTemplateName, settings.DefaultTemplateName);
        var broadcastName = FirstNonBlank(job.WatiBroadcastName, settings.DefaultBroadcastName, templateName);
        var parameters = TemplateParameter.Merge(
            settings.DefaultParameters.Select(p => new TemplateParameter(p.Name, p.Value)),
            TemplateParameter.FromJson(job.WatiParametersJson));

        var rows = await db.InputFileNumbers
            .Where(n => n.JobId == job.Id
                        && (n.Classification == NumberClassification.Fresh || n.Classification == NumberClassification.AlreadyChecked))
            .OrderBy(n => n.Sequence)
            .Select(n => new
            {
                PhoneNumberId = n.PhoneNumberId!.Value,
                n.PhoneNumber!.Digits,
                Status = n.FinalWhatsAppStatus ?? WhatsAppStatus.Unknown,
                n.CheckNumberTaskId,
            })
            .ToListAsync(cancellationToken);

        var taskIds = await db.CheckNumberTasks
            .Where(t => t.JobId == job.Id && t.ProviderTaskId != null)
            .ToDictionaryAsync(t => t.Id, t => t.ProviderTaskId!, cancellationToken);

        var candidates = rows
            .Select(r => new WatiCandidate(
                r.PhoneNumberId, r.Digits, r.Status, r.CheckNumberTaskId,
                r.CheckNumberTaskId is { } taskId && taskIds.TryGetValue(taskId, out var providerId) ? providerId : null))
            .ToList();

        return await CreateAsync(
            job.TenantId, WatiJobSource.Automatic, job.Id, commandId: null, job.CampaignId,
            templateName, templateLabel: null, broadcastName, parameters, candidates,
            job.CreatedByUserId, job.CreatedByName, cancellationToken);
    }

    /// <summary>
    /// A broadcast from the Wati page. Only numbers that THIS organisation has verified as
    /// WhatsApp numbers (its "ready in Wati" pool) can be messaged; anything else is recorded as
    /// NOT_IN_POOL.
    /// </summary>
    public async Task<WatiJob> CreateManualAsync(WorkerCommand command, SendWatiBroadcastPayload payload, CancellationToken cancellationToken)
    {
        var requested = payload.Recipients
            .Select(raw => PhoneNumberNormalizer.Normalise(raw ?? string.Empty, (raw ?? string.Empty).TrimStart().StartsWith('+')))
            .Where(result => result.Ok)
            .Select(result => result.E164!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // The organisation's verified numbers among the requested ones.
        var pool = await db.InputFileNumbers
            .Where(n => n.TenantId == command.TenantId
                        && n.NormalizedNumber != null
                        && requested.Contains(n.NormalizedNumber)
                        && n.FinalWhatsAppStatus != null)
            .Select(n => new
            {
                n.NormalizedNumber,
                PhoneNumberId = n.PhoneNumberId!.Value,
                n.PhoneNumber!.Digits,
                RegistryStatus = n.PhoneNumber.WhatsAppStatus,
                n.CheckNumberTaskId,
                n.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var byNumber = pool
            .GroupBy(p => p.NormalizedNumber!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAtUtc).First(), StringComparer.Ordinal);

        var candidates = new List<WatiCandidate>();
        var notInPool = new List<string>();

        foreach (var number in requested)
        {
            if (byNumber.TryGetValue(number, out var entry))
            {
                candidates.Add(new WatiCandidate(entry.PhoneNumberId, entry.Digits, entry.RegistryStatus, entry.CheckNumberTaskId, null));
            }
            else
            {
                notInPool.Add(number);
            }
        }

        var parameters = TemplateParameter.Merge([], payload.Parameters);

        var watiJob = await CreateAsync(
            command.TenantId, WatiJobSource.Manual, integrationJobId: null, command.Id, payload.CampaignId,
            payload.TemplateName, payload.TemplateLabel, payload.BroadcastName, parameters, candidates,
            command.RequestedByUserId, command.RequestedByName, cancellationToken,
            notInPool);

        return watiJob;
    }

    private async Task<WatiJob> CreateAsync(
        Guid tenantId,
        WatiJobSource source,
        Guid? integrationJobId,
        Guid? commandId,
        Guid? campaignId,
        string templateName,
        string? templateLabel,
        string broadcastName,
        IReadOnlyList<TemplateParameter> parameters,
        IReadOnlyList<WatiCandidate> candidates,
        Guid? requestedByUserId,
        string? requestedByName,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? notInPool = null)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var template = string.IsNullOrWhiteSpace(templateName)
            ? null
            : await db.WatiTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ElementName == templateName, cancellationToken);

        var planned = string.IsNullOrWhiteSpace(templateName)
            ? candidates.Select(c => new PlannedMessage(c, WatiMessageStatus.Skipped, null)).ToList()
            : await planner.PlanAsync(tenantId, campaignId, templateName, candidates, cancellationToken);

        var watiJob = new WatiJob
        {
            Reference = await references.NextAsync(ReferencePrefixes.WatiJob, cancellationToken),
            TenantId = tenantId,
            Source = source,
            IntegrationJobId = integrationJobId,
            CommandId = commandId,
            CampaignId = campaignId,
            TemplateName = templateName,
            TemplateLabel = FirstNonBlank(templateLabel, template?.DisplayLabel, templateName),
            BroadcastName = broadcastName,
            ParametersJson = TemplateParameter.ToJson(parameters),
            RenderedPreview = Text.Truncate(WatiTemplateRenderer.Render(template?.Body, parameters), 4000),
            ChannelNumber = settings.ChannelNumber,
            BatchSize = settings.BatchSize,
            RequestedByUserId = requestedByUserId,
            RequestedByName = requestedByName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        foreach (var plan in planned)
        {
            var row = new WatiMessage
            {
                TenantId = tenantId,
                WatiJobId = watiJob.Id,
                IntegrationJobId = integrationJobId,
                CheckNumberTaskId = plan.Candidate.CheckNumberTaskId,
                CheckNumberProviderTaskId = plan.Candidate.CheckNumberProviderTaskId,
                PhoneNumberId = plan.Candidate.PhoneNumberId,
                CampaignId = campaignId,
                WhatsAppNumber = plan.Candidate.Digits,
                WhatsAppCheck = plan.Candidate.WhatsAppStatus,
                TemplateName = templateName,
                BroadcastName = broadcastName,
                ParametersJson = watiJob.ParametersJson,
                Status = plan.Status,
                SkipReason = plan.SkipReason,
                QueuedAtUtc = now,
                UpdatedAtUtc = now,
            };
            row.LocalMessageId = "ydot-" + row.Id.ToString("N");
            watiJob.Messages.Add(row);
        }

        if (string.IsNullOrWhiteSpace(templateName))
        {
            watiJob.Status = WatiJobStatus.Failed;
            watiJob.ErrorCode = ErrorCodes.WatiTemplateMissing;
            watiJob.ErrorMessage = "No WATI template was given and Wati:DefaultTemplateName is empty, so nothing was sent.";
            watiJob.CompletedAtUtc = now;

            foreach (var row in watiJob.Messages.Where(m => m.Status == WatiMessageStatus.Skipped && m.SkipReason is null))
            {
                row.Status = WatiMessageStatus.Failed;
                row.FailedCode = ErrorCodes.WatiTemplateMissing;
                row.ErrorMessage = watiJob.ErrorMessage;
                row.FailedAtUtc = now;
            }
        }

        watiJob.TotalRecipients = watiJob.Messages.Count;
        watiJob.QueuedCount = watiJob.Messages.Count(m => m.Status == WatiMessageStatus.Queued);
        watiJob.SkippedCount = watiJob.Messages.Count(m => m.Status == WatiMessageStatus.Skipped);
        watiJob.FailedCount = watiJob.Messages.Count(m => m.Status == WatiMessageStatus.Failed);

        if (watiJob.Status == WatiJobStatus.Queued && watiJob.QueuedCount == 0)
        {
            // Nothing qualified: the job is complete as soon as it exists.
            watiJob.Status = WatiJobStatus.Completed;
            watiJob.CompletedAtUtc = now;
        }

        db.WatiJobs.Add(watiJob);
        await db.SaveChangesAsync(cancellationToken);

        var skippedByReason = watiJob.Messages
            .Where(m => m.Status == WatiMessageStatus.Skipped && m.SkipReason is not null)
            .GroupBy(m => m.SkipReason!)
            .ToDictionary(g => g.Key, g => g.Count());

        var reasons = skippedByReason.Count == 0
            ? string.Empty
            : " Skipped: " + string.Join(", ", skippedByReason.Select(kv => $"{kv.Value} {kv.Key}")) + ".";
        var poolNote = notInPool is { Count: > 0 }
            ? $" {notInPool.Count} requested number(s) are not WhatsApp-verified numbers of this organisation and were left out ({SkipReasons.NotInPool})."
            : string.Empty;

        var message = $"WATI job {watiJob.Reference} created: template {templateName}, broadcast {broadcastName}, "
                      + $"{watiJob.QueuedCount} to send, {watiJob.SkippedCount} skipped.{reasons}{poolNote}";

        logger.LogInformation("{Message}", message);

        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.WatiJobCreated,
            Message = message,
            TenantId = tenantId,
            JobId = integrationJobId,
            WatiJobId = watiJob.Id,
            StepCode = StepCodes.WatiPrepare,
            Data = new
            {
                watiJob.Reference,
                templateName,
                broadcastName,
                watiJob.QueuedCount,
                watiJob.SkippedCount,
                skippedByReason,
                notInPool = notInPool?.Count ?? 0,
                safety = new { settings.MaxRecipientsPerJob, allowList = settings.AllowedNumbers.Count },
            },
        }, cancellationToken);

        return watiJob;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
