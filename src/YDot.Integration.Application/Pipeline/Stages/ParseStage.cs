using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.PhoneNumbers;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline.Stages;

/// <summary>
/// PARSE_FILE: reads the file, normalises every number, finds duplicates and - the cost saving -
/// looks every distinct number up in the registry, so a number CheckNumber.ai already answered is
/// reused instead of paid for again.
///
/// Writes one int_input_file_numbers row per occurrence (fresh, already checked, duplicate,
/// invalid) and the job's counts. Safe to run twice: the job's rows are deleted and rewritten
/// inside the same transaction.
/// </summary>
public sealed class ParseStage(
    IIntegrationDbContext db,
    IDataPaths paths,
    INumberFileReader reader,
    IPhoneNumberRegistry registry,
    IJobJournal journal,
    IOptions<CheckNumberOptions> checkNumberOptions,
    TimeProvider time,
    ILogger<ParseStage> logger) : IJobStage
{
    private const int InsertChunkSize = 2000;

    public IReadOnlyCollection<IntegrationJobStatus> Handles { get; } =
        [IntegrationJobStatus.Queued, IntegrationJobStatus.Parsing];

    public async Task<StageOutcome> ExecuteAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        var file = job.InputFile ?? throw new InvalidOperationException($"Job {job.JobNumber} has no input file loaded.");
        var now = time.GetUtcNow();

        if (job.Status == IntegrationJobStatus.Queued)
        {
            job.StartedAtUtc ??= now;
            job.ProcessingAttempts = Math.Max(job.ProcessingAttempts, 1);
        }

        job.Status = IntegrationJobStatus.Parsing;
        job.CurrentStep = StepCodes.ParseFile;
        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var stepId = await journal.StepStartedAsync(job.Id, StepCodes.ParseFile, $"Reading {file.OriginalFileName}.", cancellationToken);

        var absolutePath = paths.ToAbsolute(file.StoredFilePath);
        if (!File.Exists(absolutePath))
        {
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "The file is missing.", ErrorCodes.FileMissing, cancellationToken: cancellationToken);
            throw new JobStageException(ErrorCodes.FileMissing,
                $"The uploaded file is no longer in the worker's data folder ({file.StoredFilePath}).");
        }

        NumberFileContents contents;
        try
        {
            contents = await reader.ReadAsync(absolutePath, file.FileKind, cancellationToken);
        }
        catch (NumberFileReadException exception)
        {
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "The file could not be read.", ErrorCodes.FileUnreadable, exception.Message, cancellationToken: cancellationToken);
            throw new JobStageException(ErrorCodes.FileUnreadable, exception.Message);
        }

        var extraction = NumberExtractor.Extract(contents);

        if (extraction.UniqueNumbers == 0)
        {
            await journal.StepFinishedAsync(stepId, StepStatus.Failed, "No valid phone number.", ErrorCodes.NoValidNumbers, cancellationToken: cancellationToken);
            throw new JobStageException(ErrorCodes.NoValidNumbers,
                $"No valid phone number was found in {file.OriginalFileName}: {extraction.TotalRows} row(s) read, "
                + $"{extraction.InvalidLines} line(s) without a usable number.");
        }

        // ---- Registry: every distinct number gets (or already has) a row ------------------------
        var distinct = extraction.DistinctNumbers.ToList();
        var registryRows = await registry.EnsureAsync(distinct.Select(PhoneNumberNormalizer.Describe).ToList(), cancellationToken);
        var saved = await ResolveSavedAnswersAsync(job, registryRows, now, cancellationToken);

        // ---- One row per occurrence, in file order ------------------------------------------------
        var rows = BuildRows(job, extraction, registryRows, saved, now);
        var fresh = rows.Count(r => r.Classification == NumberClassification.Fresh);
        var reused = rows.Count(r => r.Classification == NumberClassification.AlreadyChecked);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.InputFileNumbers.Where(n => n.JobId == job.Id).ExecuteDeleteAsync(cancellationToken);

            foreach (var chunk in rows.Chunk(InsertChunkSize))
            {
                db.InputFileNumbers.AddRange(chunk);
                await db.SaveChangesAsync(cancellationToken);

                // Stop tracking what was just written, so a 50,000-number file does not keep
                // 50,000 entities in the change tracker (the job and file stay tracked).
                foreach (var entry in db.ChangeTracker.Entries<InputFileNumber>().ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }

            file.SheetCount = contents.Sheets.Count;
            job.TotalRows = extraction.TotalRows;
            job.NumbersFound = extraction.NumbersFound;
            job.UniqueNumbers = extraction.UniqueNumbers;
            job.DuplicateNumbers = extraction.DuplicateNumbers;
            job.InvalidLines = extraction.InvalidLines;
            job.FreshNumbers = fresh;
            job.ReusedNumbers = reused;
            job.CheckedNumbers = 0;
            job.ConsecutiveFailures = 0;
            job.Status = fresh > 0 && job.RunCheckNumber
                ? IntegrationJobStatus.CheckingNumbers
                : IntegrationJobStatus.Verifying;
            job.UpdatedAtUtc = time.GetUtcNow();

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var summary =
            $"{extraction.TotalRows} row(s) read from {contents.Sheets.Count} sheet(s): {extraction.UniqueNumbers} distinct number(s), "
            + $"{fresh} to check, {reused} answered from earlier checks, {extraction.DuplicateNumbers} duplicate(s), "
            + $"{extraction.InvalidLines} invalid line(s).";

        logger.LogInformation("Job {JobNumber}: {Summary}", job.JobNumber, summary);

        await journal.StepFinishedAsync(stepId, StepStatus.Succeeded, summary, details: new
        {
            extraction.TotalRows,
            sheets = extraction.Sheets,
            extraction.NumbersFound,
            extraction.UniqueNumbers,
            fresh,
            reused,
            extraction.DuplicateNumbers,
            extraction.InvalidLines,
        }, cancellationToken: cancellationToken);

        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.ParseCompleted,
            Message = summary,
            TenantId = job.TenantId,
            InputFileId = job.InputFileId,
            JobId = job.Id,
            StepCode = StepCodes.ParseFile,
        }, cancellationToken);

        if (reused > 0)
        {
            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.NumbersReused,
                Message = $"{reused} number(s) already have a WhatsApp answer from an earlier check; they are not sent to CheckNumber.ai again.",
                TenantId = job.TenantId,
                JobId = job.Id,
                StepCode = StepCodes.ParseFile,
                Data = new { reused, scope = checkNumberOptions.Value.ReuseScope.ToString(), checkNumberOptions.Value.ResultValidityDays },
            }, cancellationToken);
        }

        if (fresh > 0 && !job.RunCheckNumber)
        {
            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.CheckNumberSkipped,
                Level = EventLevel.Warning,
                Message = $"{fresh} number(s) have no saved answer, but CheckNumber.ai is switched off for this job; they stay unverified.",
                TenantId = job.TenantId,
                JobId = job.Id,
                StepCode = StepCodes.ParseFile,
            }, cancellationToken);
        }

        var minimum = checkNumberOptions.Value.MinimumNumbersPerTask;
        if (fresh > 0 && job.RunCheckNumber && fresh < minimum)
        {
            throw new JobStageException(ErrorCodes.BelowMinimumNumbers,
                $"{fresh} number(s) need checking but CheckNumber:MinimumNumbersPerTask is {minimum}. "
                + "Nothing was sent to CheckNumber.ai. Lower the setting or add more numbers to the file.");
        }

        return StageOutcome.Continue;
    }

    /// <summary>The saved Yes / No answers this job may reuse, keyed by E.164.</summary>
    private async Task<Dictionary<string, SavedAnswer>> ResolveSavedAnswersAsync(
        IntegrationJob job,
        IReadOnlyDictionary<string, RegistryEntry> registryRows,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var answers = new Dictionary<string, SavedAnswer>(StringComparer.Ordinal);

        if (job.ForceRecheck)
        {
            return answers;
        }

        var settings = checkNumberOptions.Value;
        DateTimeOffset? notBefore = settings.ResultValidityDays > 0 ? now.AddDays(-settings.ResultValidityDays) : null;

        if (settings.ReuseScope == ReuseScope.Global)
        {
            foreach (var (e164, entry) in registryRows)
            {
                if (entry.Status is WhatsAppStatus.Yes or WhatsAppStatus.No
                    && entry.CheckedAtUtc is { } checkedAt
                    && entry.LastResultId is { } resultId
                    && (notBefore is null || checkedAt >= notBefore))
                {
                    answers[e164] = new SavedAnswer(entry.Id, entry.Status, checkedAt, resultId);
                }
            }

            return answers;
        }

        var byId = registryRows.Values.ToDictionary(entry => entry.Id, entry => entry.E164);
        var tenantAnswers = await registry.GetTenantAnswersAsync(job.TenantId, byId.Keys.ToList(), cancellationToken);

        foreach (var (phoneId, answer) in tenantAnswers)
        {
            if (notBefore is null || answer.CheckedAtUtc >= notBefore)
            {
                answers[byId[phoneId]] = answer;
            }
        }

        return answers;
    }

    private static List<InputFileNumber> BuildRows(
        IntegrationJob job,
        ExtractionResult extraction,
        IReadOnlyDictionary<string, RegistryEntry> registryRows,
        Dictionary<string, SavedAnswer> saved,
        DateTimeOffset now)
    {
        var rows = new List<InputFileNumber>(extraction.Occurrences.Count);
        var firstOccurrence = new Dictionary<string, InputFileNumber>(StringComparer.Ordinal);

        foreach (var occurrence in extraction.Occurrences)
        {
            var row = new InputFileNumber
            {
                TenantId = job.TenantId,
                InputFileId = job.InputFileId,
                JobId = job.Id,
                Sequence = rows.Count,
                SheetName = Truncate(occurrence.SheetName, 100),
                LineNumber = occurrence.LineNumber,
                RawValue = Truncate(occurrence.RawValue, 500),
                CreatedAtUtc = now,
            };

            if (occurrence.IsInvalid)
            {
                row.Classification = NumberClassification.Invalid;
                row.InvalidReason = Truncate(occurrence.InvalidReason ?? "Not a phone number", 300);
                rows.Add(row);
                continue;
            }

            var e164 = occurrence.E164!;
            var entry = registryRows[e164];
            var country = CountryDirectory.Of(e164);

            row.NormalizedNumber = e164;
            row.ContactName = occurrence.ContactName;
            row.PhoneNumberId = entry.Id;
            row.CountryIso = country.Iso;
            row.CountryName = country.Name;
            row.CountryCode = country.Code.Length > 0 ? country.Code : null;

            if (occurrence.IsDuplicate)
            {
                row.Classification = NumberClassification.Duplicate;
                row.DuplicateOfId = firstOccurrence[e164].Id;
            }
            else if (saved.TryGetValue(e164, out var answer))
            {
                row.Classification = NumberClassification.AlreadyChecked;
                row.ReusedWhatsAppStatus = answer.Status;
                row.ReusedCheckedAtUtc = answer.CheckedAtUtc;
                row.ReusedFromResultId = answer.ResultId;
                firstOccurrence[e164] = row;
            }
            else
            {
                row.Classification = NumberClassification.Fresh;
                firstOccurrence[e164] = row;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
