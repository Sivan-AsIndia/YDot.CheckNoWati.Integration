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

/// <summary>A selected number that did not become a message row, and why (int_wati_jobs.excluded_json).</summary>
public sealed record ExcludedRecipient(string Number, string Reason, string? Detail);

/// <summary>
/// Creates a WATI job and its message rows from a person's selection on the WhatsApp Messages page.
///
/// THE SELECTION IS THE WHOLE AUDIENCE. A check verifies hundreds of numbers; the person picks the
/// ten or fifty that should hear from the organisation, and only those can ever become messages.
/// Each selected number gets a message row - Queued when it will be sent, Skipped with the reason
/// when a safety rule leaves it out - or, when it is not one of this organisation's checked
/// numbers at all, an entry in the job's excluded list. Nothing is added.
///
/// EVERY MESSAGE IS TRACEABLE TO ITS CHECK. The row records the check job the number was picked
/// from (or the newest one that verified it), that job's number, the CheckNumber.ai task whose
/// answer is behind it, and the contact name the file gave.
///
/// (Until 15 October 2026 this factory also built an AUTOMATIC job for every finished check,
/// messaging all of its verified numbers. That path is gone: see WatiStage.)
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
    private const int MaxTags = 10;
    private const int MaxTagLength = 40;

    /// <summary>A broadcast from the Wati page: the selected numbers, the template and how to fill it.</summary>
    public async Task<WatiJob> CreateManualAsync(WorkerCommand command, SendWatiBroadcastPayload payload, CancellationToken cancellationToken)
    {
        // ---- 1. The selection as the person made it -------------------------------------------------
        // An older web application sends only the plain list of numbers; a current one sends each
        // number with the check it was picked from.
        var requested = payload.Selection is { Count: > 0 } selection
            ? selection.Select(entry => (Raw: entry.Number ?? string.Empty, entry.SourceJobId)).ToList()
            : payload.Recipients.Select(raw => (Raw: raw ?? string.Empty, SourceJobId: (Guid?)null)).ToList();

        var excluded = new List<ExcludedRecipient>();
        var wanted = new List<(string E164, Guid? SourceJobId)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (raw, sourceJobId) in requested)
        {
            var text = raw.Trim();
            var normalised = PhoneNumberNormalizer.Normalise(text, text.StartsWith('+'));

            if (!normalised.Ok)
            {
                excluded.Add(new ExcludedRecipient(Text.Truncate(text, 40) ?? string.Empty, SkipReasons.InvalidNumber, normalised.Reason));
            }
            else if (!seen.Add(normalised.E164!))
            {
                excluded.Add(new ExcludedRecipient(normalised.E164!, SkipReasons.DuplicateInJob, "Selected more than once; it is messaged once."));
            }
            else
            {
                wanted.Add((normalised.E164!, sourceJobId));
            }
        }

        // ---- 2. What this organisation's checks said about each of them ------------------------------
        var numbers = wanted.Select(entry => entry.E164).ToList();
        var rows = numbers.Count == 0
            ? []
            : await db.InputFileNumbers.AsNoTracking()
                .Where(n => n.TenantId == command.TenantId
                            && n.NormalizedNumber != null
                            && numbers.Contains(n.NormalizedNumber)
                            && n.PhoneNumberId != null
                            && n.FinalWhatsAppStatus != null)
                .Select(n => new CheckedRow(
                    n.NormalizedNumber!,
                    n.JobId,
                    n.PhoneNumberId!.Value,
                    n.PhoneNumber!.Digits,
                    n.PhoneNumber.WhatsAppStatus,
                    n.FinalWhatsAppStatus!.Value,
                    n.Classification,
                    n.CheckResultId,
                    n.ContactName,
                    n.CreatedAtUtc))
                .ToListAsync(cancellationToken);

        var byNumber = rows
            .GroupBy(row => row.E164, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        // The CheckNumber.ai task behind each answer: the answer's result row names its task, for a
        // fresh answer and a reused one alike (a reused answer points at the earlier check's result).
        var resultIds = rows.Where(row => row.CheckResultId != null).Select(row => row.CheckResultId!.Value).Distinct().ToList();
        var taskOfResult = resultIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.CheckNumberResults.AsNoTracking()
                .Where(result => resultIds.Contains(result.Id))
                .ToDictionaryAsync(result => result.Id, result => result.CheckNumberTaskId, cancellationToken);
        var taskIds = taskOfResult.Values.Distinct().ToList();
        var providerTaskIds = taskIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await db.CheckNumberTasks.AsNoTracking()
                .Where(task => taskIds.Contains(task.Id))
                .ToDictionaryAsync(task => task.Id, task => task.ProviderTaskId, cancellationToken);

        // ---- 3. How each recipient's template values are filled ----------------------------------------
        var parameters = TemplateParameter.Merge([], payload.Parameters);
        var bindings = (payload.Bindings ?? [])
            .Where(binding => !string.IsNullOrWhiteSpace(binding.Name) && ParameterSources.IsKnown(binding.Source))
            .GroupBy(binding => binding.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ParameterBinding(group.Key, group.Last().Source))
            .ToList();

        // ---- 4. One candidate per selected number that a check of this organisation answered -------------
        var chosen = new List<(string E164, CheckedRow Row, string? ContactName)>();

        foreach (var (e164, sourceJobId) in wanted)
        {
            if (!byNumber.TryGetValue(e164, out var checkedRows))
            {
                excluded.Add(new ExcludedRecipient(e164, SkipReasons.NotInPool,
                    "Not a number any check of this organisation has answered, so it cannot be messaged from here."));
                continue;
            }

            var row = PickRow(checkedRows, sourceJobId);

            // The file the number was picked from may have held no name beside it while another of
            // the organisation's files did: the newest name the organisation has for the number is used.
            var contactName = !string.IsNullOrWhiteSpace(row.ContactName)
                ? row.ContactName
                : checkedRows.Where(r => !string.IsNullOrWhiteSpace(r.ContactName)).OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault()?.ContactName;

            chosen.Add((e164, row, contactName));
        }

        var jobIds = chosen.Select(entry => entry.Row.JobId).Distinct().ToList();
        var jobNumbers = jobIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.IntegrationJobs.AsNoTracking()
                .Where(job => jobIds.Contains(job.Id))
                .ToDictionaryAsync(job => job.Id, job => job.JobNumber, cancellationToken);

        var candidates = chosen
            .Select(entry =>
            {
                Guid? taskId = entry.Row.CheckResultId is { } resultId && taskOfResult.TryGetValue(resultId, out var task) ? task : null;

                return new WatiCandidate(
                    entry.Row.PhoneNumberId,
                    entry.Row.Digits,

                    // THE CURRENT ANSWER decides, not the one in the file the number was picked from:
                    // a number a later check found is no longer on WhatsApp is not messaged.
                    entry.Row.RegistryStatus,
                    taskId,
                    taskId is { } id ? providerTaskIds.GetValueOrDefault(id) : null,
                    entry.Row.JobId,
                    jobNumbers.GetValueOrDefault(entry.Row.JobId),
                    Text.Truncate(entry.ContactName, ContactNameReader.MaxLength),
                    bindings.Count == 0 ? null : Personalise(parameters, bindings, entry.ContactName, entry.E164));
            })
            .ToList();

        return await CreateAsync(
            command,
            payload,
            parameters,
            bindings,
            candidates,
            excluded,
            requestedCount: requested.Count,
            cancellationToken);
    }

    /// <summary>
    /// The row that stands for a number: the one from the check the person picked it from, when
    /// that check answered it; otherwise the newest check that found it on WhatsApp; otherwise the
    /// newest answer of any kind (the planner then leaves it out with the reason).
    /// </summary>
    private static CheckedRow PickRow(List<CheckedRow> rows, Guid? sourceJobId)
    {
        static IEnumerable<CheckedRow> FirstSightingFirst(IEnumerable<CheckedRow> candidates) =>
            candidates
                .OrderBy(row => row.Classification == NumberClassification.Duplicate ? 1 : 0)
                .ThenByDescending(row => row.CreatedAtUtc);

        if (sourceJobId is { } jobId && FirstSightingFirst(rows.Where(row => row.JobId == jobId)).FirstOrDefault() is { } picked)
        {
            return picked;
        }

        return FirstSightingFirst(rows.Where(row => row.FinalStatus == WhatsAppStatus.Yes)).FirstOrDefault()
               ?? FirstSightingFirst(rows).First();
    }

    /// <summary>
    /// One recipient's template values: the job's values, with each bound parameter replaced by the
    /// recipient's own - the contact name, or the number. A recipient the source has nothing for
    /// keeps the job's value, which the page asks for as the fallback ("Friend").
    /// </summary>
    public static IReadOnlyList<TemplateParameter> Personalise(
        IReadOnlyList<TemplateParameter> parameters,
        IReadOnlyList<ParameterBinding> bindings,
        string? contactName,
        string e164)
    {
        var overrides = new List<TemplateParameter>();

        foreach (var binding in bindings)
        {
            var value = binding.Source switch
            {
                ParameterSources.ContactName => contactName,
                ParameterSources.PhoneNumber => e164,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides.Add(new TemplateParameter(binding.Name, value.Trim()));
            }
        }

        return TemplateParameter.Merge(parameters, overrides);
    }

    private async Task<WatiJob> CreateAsync(
        WorkerCommand command,
        SendWatiBroadcastPayload payload,
        IReadOnlyList<TemplateParameter> parameters,
        IReadOnlyList<ParameterBinding> bindings,
        IReadOnlyList<WatiCandidate> candidates,
        IReadOnlyList<ExcludedRecipient> excluded,
        int requestedCount,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var templateName = payload.TemplateName?.Trim() ?? string.Empty;
        var broadcastName = payload.BroadcastName?.Trim() ?? string.Empty;
        var template = string.IsNullOrWhiteSpace(templateName)
            ? null
            : await db.WatiTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ElementName == templateName, cancellationToken);

        var planned = string.IsNullOrWhiteSpace(templateName)
            ? candidates.Select(c => new PlannedMessage(c, WatiMessageStatus.Skipped, null)).ToList()
            : await planner.PlanAsync(command.TenantId, payload.CampaignId, templateName, candidates, cancellationToken);

        var tags = CleanTags(payload.Tags);

        var watiJob = new WatiJob
        {
            Reference = await references.NextAsync(ReferencePrefixes.WatiJob, cancellationToken),
            TenantId = command.TenantId,
            Source = WatiJobSource.Manual,
            CommandId = command.Id,
            CampaignId = payload.CampaignId,
            TemplateName = templateName,
            TemplateLabel = FirstNonBlank(payload.TemplateLabel, template?.DisplayLabel, templateName),
            BroadcastName = broadcastName,
            ParametersJson = TemplateParameter.ToJson(parameters),
            RenderedPreview = Text.Truncate(WatiTemplateRenderer.Render(template?.Body, parameters), 4000),
            TemplateBody = Text.Truncate(template?.Body, 4000),
            TemplateHeaderJson = template?.HeaderJson,
            ParameterBindingsJson = bindings.Count == 0 ? null : JsonDefaults.Serialize(bindings.Select(b => new { name = b.Name, source = b.Source })),
            RequestedCount = requestedCount,
            ExcludedJson = excluded.Count == 0 ? null : JsonDefaults.Serialize(excluded.Select(e => new { number = e.Number, reason = e.Reason, detail = e.Detail })),
            RecipientGroupId = payload.RecipientGroupId,
            RecipientGroupName = Text.Truncate(Blank(payload.RecipientGroupName), 200),
            TagsJson = tags.Count == 0 ? null : JsonDefaults.Serialize(tags),
            ChannelNumber = settings.ChannelNumber,
            BatchSize = settings.BatchSize,
            RequestedByUserId = command.RequestedByUserId,
            RequestedByName = command.RequestedByName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        foreach (var plan in planned)
        {
            var candidate = plan.Candidate;
            var row = new WatiMessage
            {
                TenantId = command.TenantId,
                WatiJobId = watiJob.Id,
                CheckNumberTaskId = candidate.CheckNumberTaskId,
                CheckNumberProviderTaskId = candidate.CheckNumberProviderTaskId,
                SourceJobId = candidate.SourceJobId,
                SourceJobNumber = candidate.SourceJobNumber,
                ContactName = candidate.ContactName,
                PhoneNumberId = candidate.PhoneNumberId,
                CampaignId = payload.CampaignId,
                WhatsAppNumber = candidate.Digits,
                WhatsAppCheck = candidate.WhatsAppStatus,
                TemplateName = templateName,
                BroadcastName = broadcastName,
                ParametersJson = candidate.Parameters is { } own ? TemplateParameter.ToJson(own) : watiJob.ParametersJson,
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
            watiJob.ErrorMessage = "No WATI template was given, so nothing was sent.";
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
        var excludedByReason = excluded
            .GroupBy(e => e.Reason)
            .ToDictionary(g => g.Key, g => g.Count());
        var sourceJobs = watiJob.Messages
            .Where(m => m.SourceJobNumber is not null)
            .Select(m => m.SourceJobNumber!)
            .Distinct()
            .OrderBy(number => number, StringComparer.Ordinal)
            .ToList();

        var reasons = skippedByReason.Count == 0
            ? string.Empty
            : " Skipped: " + string.Join(", ", skippedByReason.Select(kv => $"{kv.Value} {kv.Key}")) + ".";
        var leftOut = excludedByReason.Count == 0
            ? string.Empty
            : " Left out of the selection: " + string.Join(", ", excludedByReason.Select(kv => $"{kv.Value} {kv.Key}")) + ".";
        var origin = watiJob.RecipientGroupName is null ? string.Empty : $" Selection from the group \"{watiJob.RecipientGroupName}\".";

        var message = $"WATI job {watiJob.Reference} created from a selection of {requestedCount} number(s): template {templateName}, "
                      + $"broadcast {broadcastName}, {watiJob.QueuedCount} to send, {watiJob.SkippedCount} skipped.{reasons}{leftOut}{origin}";

        logger.LogInformation("{Message}", message);

        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.WatiJobCreated,
            Message = message,
            TenantId = command.TenantId,
            WatiJobId = watiJob.Id,
            StepCode = StepCodes.WatiPrepare,
            Data = new
            {
                watiJob.Reference,
                command = command.Reference,
                templateName,
                broadcastName,
                requested = requestedCount,
                watiJob.QueuedCount,
                watiJob.SkippedCount,
                skippedByReason,
                excludedByReason,
                sourceJobs,
                group = watiJob.RecipientGroupName,
                tags,
                bindings = bindings.Select(b => new { b.Name, b.Source }),
                safety = new { settings.MaxRecipientsPerJob, allowList = settings.EffectiveAllowedNumbers().Count },
            },
        }, cancellationToken);

        return watiJob;
    }

    /// <summary>Trimmed, without repeats (ignoring case), at most ten of at most forty characters.</summary>
    private static List<string> CleanTags(IReadOnlyList<string>? tags) =>
        (tags ?? [])
            .Select(tag => (tag ?? string.Empty).Trim())
            .Where(tag => tag.Length > 0)
            .Select(tag => tag.Length > MaxTagLength ? tag[..MaxTagLength] : tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTags)
            .ToList();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    /// <summary>One of the organisation's answers for a selected number, as the query reads it.</summary>
    private sealed record CheckedRow(
        string E164,
        Guid JobId,
        Guid PhoneNumberId,
        string Digits,
        WhatsAppStatus RegistryStatus,
        WhatsAppStatus FinalStatus,
        NumberClassification Classification,
        Guid? CheckResultId,
        string? ContactName,
        DateTimeOffset CreatedAtUtc);
}
