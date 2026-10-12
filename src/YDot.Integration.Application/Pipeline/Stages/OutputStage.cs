using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline.Stages;

/// <summary>
/// GENERATE_OUTPUT: writes the completed output and closes the job.
///
///   data/output/{job}/whatsapp-results.csv   one line per line of the input file, with every
///                                            decision: normalised number, WhatsApp answer and
///                                            where it came from, WATI status or skip reason
///   data/output/{job}/summary.json           counts, CheckNumber.ai task ids and costs, the WATI
///                                            job and its outcome, timings
/// </summary>
public sealed class OutputStage(
    IIntegrationDbContext db,
    IDataPaths paths,
    IJobJournal journal,
    IOptions<OutputOptions> options,
    TimeProvider time,
    ILogger<OutputStage> logger) : IJobStage
{
    public IReadOnlyCollection<IntegrationJobStatus> Handles { get; } = [IntegrationJobStatus.GeneratingOutput];

    public async Task<StageOutcome> ExecuteAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        job.CurrentStep = StepCodes.GenerateOutput;
        var stepId = await journal.StepStartedAsync(job.Id, StepCodes.GenerateOutput, "Writing the completed output.", cancellationToken);

        var file = job.InputFile!;
        var tasks = await db.CheckNumberTasks.AsNoTracking()
            .Where(t => t.JobId == job.Id)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);
        var watiJob = await db.WatiJobs.AsNoTracking()
            .Where(w => w.IntegrationJobId == job.Id)
            .OrderByDescending(w => w.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var folder = paths.OutputFolder(job.JobNumber);
        Directory.CreateDirectory(folder);

        if (options.Value.WriteResultsCsv)
        {
            var csvPath = Path.Combine(folder, "whatsapp-results.csv");
            await WriteCsvAsync(job, watiJob, tasks, csvPath, cancellationToken);
            job.ResultsCsvPath = paths.ToRelative(csvPath);
        }

        var now = time.GetUtcNow();
        var failedSends = watiJob is { Status: WatiJobStatus.Failed or WatiJobStatus.CompletedWithErrors };
        job.Status = failedSends ? IntegrationJobStatus.CompletedWithErrors : IntegrationJobStatus.Completed;
        job.CompletedAtUtc = now;
        job.NextAttemptAtUtc = null;
        job.OutcomeMessage = BuildOutcome(job, watiJob);
        job.WorkingDirectory = paths.ToRelative(folder);
        job.UpdatedAtUtc = now;

        if (options.Value.WriteSummaryJson)
        {
            var summaryPath = Path.Combine(folder, "summary.json");
            // No byte-order mark: summary.json is read by programs, and many JSON parsers reject one.
            // (The CSV beside it keeps its mark, because that is what makes Excel read it as UTF-8.)
            await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(Summary(job, file, tasks, watiJob), JsonDefaults.Indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            job.SummaryJsonPath = paths.ToRelative(summaryPath);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Job {JobNumber} {Status}: {Outcome}", job.JobNumber, job.Status, job.OutcomeMessage);
        await journal.StepFinishedAsync(stepId, StepStatus.Succeeded, $"Output written to {job.WorkingDirectory}.",
            details: new { job.ResultsCsvPath, job.SummaryJsonPath }, cancellationToken: cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.OutputWritten,
            Message = $"Completed output written: {job.ResultsCsvPath ?? "(csv disabled)"}, {job.SummaryJsonPath ?? "(summary disabled)"}.",
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = StepCodes.GenerateOutput,
        }, cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.JobCompleted,
            Level = job.Status == IntegrationJobStatus.Completed ? EventLevel.Information : EventLevel.Warning,
            Message = $"Job {job.JobNumber} {(job.Status == IntegrationJobStatus.Completed ? "completed" : "completed with errors")}: {job.OutcomeMessage}",
            TenantId = job.TenantId,
            InputFileId = job.InputFileId,
            JobId = job.Id,
            StepCode = StepCodes.CompleteJob,
        }, cancellationToken);

        return StageOutcome.Stop;
    }

    /// <summary>The "why it succeeded" sentence of the report, in the upload page's style.</summary>
    public static string BuildOutcome(IntegrationJob job, WatiJob? watiJob)
    {
        var parts = new List<string>();

        if (job.FreshNumbers > 0)
        {
            parts.Add(job.RunCheckNumber
                ? $"{Count(job.FreshNumbers)} number{Plural(job.FreshNumbers)} processed by CheckNumber.ai"
                : $"{Count(job.FreshNumbers)} number{Plural(job.FreshNumbers)} not checked (CheckNumber.ai switched off)");
        }

        if (job.ReusedNumbers > 0)
        {
            parts.Add($"{Count(job.ReusedNumbers)} answered from earlier checks");
        }

        parts.Add($"{Count(job.WhatsAppYes)} on WhatsApp");

        if (job.WhatsAppUnknown > 0)
        {
            parts.Add($"{Count(job.WhatsAppUnknown)} could not be verified");
        }

        if (watiJob is null)
        {
            parts.Add(job.RunWati ? "no WATI messages" : "WATI not run for this job");
        }
        else if (watiJob.Status == WatiJobStatus.Failed && watiJob.AcceptedCount == 0 && watiJob.FailedCount > 0)
        {
            parts.Add($"WATI messages failed: {watiJob.ErrorMessage ?? "see the WATI job"}");
        }
        else
        {
            var sent = $"{Count(watiJob.AcceptedCount)} WhatsApp message{Plural(watiJob.AcceptedCount)} sent";
            if (watiJob.FailedCount > 0)
            {
                sent += $", {Count(watiJob.FailedCount)} failed";
            }

            if (watiJob.SkippedCount > 0)
            {
                sent += $", {Count(watiJob.SkippedCount)} skipped";
            }

            parts.Add(sent);
        }

        return string.Join(" · ", parts);
    }

    private async Task WriteCsvAsync(
        IntegrationJob job, WatiJob? watiJob, IReadOnlyList<CheckNumberTask> tasks, string path, CancellationToken cancellationToken)
    {
        var providerTaskIds = tasks.Where(t => t.ProviderTaskId != null).ToDictionary(t => t.Id, t => t.ProviderTaskId!);
        var messages = watiJob is null
            ? new Dictionary<Guid, WatiMessage>()
            : await db.WatiMessages.AsNoTracking()
                .Where(m => m.WatiJobId == watiJob.Id)
                .ToDictionaryAsync(m => m.PhoneNumberId, cancellationToken);

        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(
            "sheet,line_number,original_value,normalized_number,country,classification,whatsapp_available,answer_source,"
            + "checknumber_task_id,note,wati_status,wati_skip_reason,wati_local_message_id,wati_error");

        var skip = 0;
        const int pageSize = 5000;
        while (true)
        {
            var page = await db.InputFileNumbers.AsNoTracking()
                .Where(n => n.JobId == job.Id)
                .OrderBy(n => n.Sequence)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var n in page)
            {
                WatiMessage? message = null;
                if (n.PhoneNumberId is { } phoneId && n.Classification != NumberClassification.Duplicate)
                {
                    messages.TryGetValue(phoneId, out message);
                }

                var whatsapp = n.FinalWhatsAppStatus switch
                {
                    WhatsAppStatus.Yes => "yes",
                    WhatsAppStatus.No => "no",
                    WhatsAppStatus.Unknown => "unknown",
                    _ => string.Empty,
                };

                var source = n.Classification switch
                {
                    NumberClassification.Fresh => job.RunCheckNumber ? "checked" : "not_checked",
                    NumberClassification.AlreadyChecked => "saved",
                    NumberClassification.Duplicate => "duplicate",
                    _ => "invalid",
                };

                var fields = new[]
                {
                    n.SheetName,
                    n.LineNumber.ToString(CultureInfo.InvariantCulture),
                    n.RawValue,
                    n.NormalizedNumber ?? string.Empty,
                    n.CountryName ?? string.Empty,
                    n.Classification.ToString(),
                    whatsapp,
                    source,
                    n.CheckNumberTaskId is { } taskId && providerTaskIds.TryGetValue(taskId, out var providerId) ? providerId : string.Empty,
                    n.InvalidReason ?? n.ResultNote ?? string.Empty,
                    message?.Status.ToString() ?? string.Empty,
                    message?.SkipReason ?? string.Empty,
                    message is { Status: not WatiMessageStatus.Skipped } ? message.LocalMessageId : string.Empty,
                    message?.ErrorMessage ?? message?.FailedReason ?? string.Empty,
                };

                await writer.WriteLineAsync(string.Join(',', fields.Select(Csv)));
            }

            skip += page.Count;
        }
    }

    private static object Summary(IntegrationJob job, InputFile file, IReadOnlyList<CheckNumberTask> tasks, WatiJob? watiJob) => new
    {
        job.JobNumber,
        job.Id,
        job.TenantId,
        organisation = file.TenantName ?? file.TenantCode,
        status = job.Status.ToString(),
        job.OutcomeMessage,
        job.ErrorCode,
        job.ErrorMessage,
        inputFile = new
        {
            file.OriginalFileName,
            file.FileKind,
            file.FileSizeBytes,
            file.ContentSha256,
            file.SheetCount,
            file.UploadReference,
            source = file.Source.ToString(),
            file.CampaignId,
            file.CampaignCode,
            file.CampaignName,
            file.UploadedByName,
        },
        counts = new
        {
            job.TotalRows,
            job.NumbersFound,
            job.UniqueNumbers,
            job.FreshNumbers,
            job.ReusedNumbers,
            job.DuplicateNumbers,
            job.InvalidLines,
            job.WhatsAppYes,
            job.WhatsAppNo,
            job.WhatsAppUnknown,
            job.WatiEligible,
            job.WatiSent,
            job.WatiFailed,
            job.WatiSkipped,
        },
        cost = new { estimated = job.EstimatedCost, actual = job.ActualCost, currency = job.CostCurrency },
        checkNumberTasks = tasks.Select(t => new
        {
            taskId = t.ProviderTaskId,
            t.Sequence,
            state = t.State.ToString(),
            t.IsSuperseded,
            t.ProviderStatus,
            t.SubmittedCount,
            t.Total,
            t.Success,
            t.Failure,
            t.CachedCount,
            t.EstimatedAmount,
            t.ActualAmount,
            t.Currency,
            t.PollCount,
            t.ProviderCreatedAtUtc,
            t.ExportedAtUtc,
            t.CompletedAtUtc,
        }),
        wati = watiJob is null
            ? null
            : new
            {
                watiJob.Reference,
                status = watiJob.Status.ToString(),
                watiJob.TemplateName,
                watiJob.BroadcastName,
                parameters = Wati.TemplateParameter.FromJson(watiJob.ParametersJson),
                watiJob.TotalRecipients,
                watiJob.QueuedCount,
                watiJob.AcceptedCount,
                watiJob.FailedCount,
                watiJob.SkippedCount,
                watiJob.ErrorCode,
                watiJob.ErrorMessage,
            },
        timings = new { job.CreatedAtUtc, job.StartedAtUtc, job.CompletedAtUtc, job.ProcessingAttempts },
    };

    private static string Csv(string value)
    {
        // Guard against spreadsheet formula injection, as the upload page's csvCell does.
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !(value[0] == '+' && value.Skip(1).All(c => char.IsAsciiDigit(c) || c == ' ')))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string Count(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));

    private static string Plural(int value) => value == 1 ? string.Empty : "s";
}
