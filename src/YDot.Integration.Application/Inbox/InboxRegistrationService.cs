using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Wati;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Inbox;

/// <summary>
/// "Worker detects the file and puts it into the database": turns a ready inbox file into an
/// int_input_files row and a Queued int_integration_jobs row.
///
///   1. READY?      stable size/time (folder drops) and openable with no sharing;
///   2. CLAIM       atomic move into files/yyyy/MM/{id}/ - a second worker instance loses the race;
///   3. IDENTIFY    SHA-256, organisation (manifest, or inbox/{subdomain}/), campaign, uploader;
///   4. DUPLICATE?  same organisation + same content + same campaign already processed -> recorded
///                  as Duplicate, no job, no CheckNumber.ai charge;
///   5. REGISTER    input file + job in ONE transaction.
///
/// A crash between 2 and 5 leaves a claimed folder with no database row; the next scan finds it
/// (claim.json says what it was) and registers it, so a file is never lost.
/// </summary>
public sealed class InboxRegistrationService(
    IIntegrationDbContext db,
    IInboxFileSystem inbox,
    IDataPaths paths,
    ITenantDirectory tenants,
    IReferenceGenerator references,
    IJobJournal journal,
    IWorkerIdentity identity,
    InboxReadinessTracker readiness,
    IOptions<WorkerOptions> workerOptions,
    IOptions<PipelineOptions> pipelineOptions,
    TimeProvider time,
    ILogger<InboxRegistrationService> logger)
{
    /// <summary>A claimed folder younger than this may still be in the middle of being registered by another instance.</summary>
    private static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromMinutes(2);

    private WorkerOptions Settings => workerOptions.Value;

    /// <summary>One pass over the inbox; returns how many files were registered.</summary>
    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        var registered = 0;

        // ---- Interrupted registrations from an earlier run ---------------------------------------
        foreach (var claimed in inbox.ListClaimedFolders())
        {
            if (!Guid.TryParse(Path.GetFileName(claimed.Folder), out var inputFileId)
                || time.GetUtcNow() - claimed.Info.ClaimedAtUtc < OrphanGracePeriod
                || await db.InputFiles.AnyAsync(f => f.Id == inputFileId, cancellationToken))
            {
                continue;
            }

            logger.LogWarning("Found {File} claimed at {ClaimedAt} but never registered; registering it now.",
                claimed.Info.OriginalFileName, claimed.Info.ClaimedAtUtc);
            await RegisterAsync(inputFileId, claimed, cancellationToken);
            registered++;
        }

        // ---- New files ------------------------------------------------------------------------------
        var candidates = inbox.ListCandidates();
        readiness.Prune(candidates);

        foreach (var candidate in candidates.OrderBy(c => c.LastWriteUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.ManifestPath is null && !Settings.AcceptFolderDrops)
            {
                continue;
            }

            if (!readiness.IsReady(candidate, TimeSpan.FromSeconds(Settings.FileStabilitySeconds)))
            {
                logger.LogDebug("{File} is still changing; it will be picked up once it is stable.", candidate.FileName);
                continue;
            }

            if (!inbox.CanOpenExclusively(candidate.FullPath))
            {
                logger.LogDebug("{File} is open in another program; it will be picked up when it is released.", candidate.FileName);
                continue;
            }

            var inputFileId = Guid.CreateVersion7();
            var claimedAt = time.GetUtcNow();
            var manifest = ReadManifest(candidate.ManifestPath);
            var originalName = SafeFileName(manifest?.OriginalFileName) ?? candidate.FileName;

            var claimed = inbox.TryClaim(
                candidate,
                paths.InputFileFolder(inputFileId, claimedAt),
                new ClaimInfo(originalName, candidate.FileName, candidate.TenantFolder, claimedAt, identity.InstanceName));

            readiness.Forget(candidate.FullPath);

            if (claimed is null)
            {
                logger.LogDebug("{File} was claimed by another worker instance.", candidate.FileName);
                continue;
            }

            await RegisterAsync(inputFileId, claimed, cancellationToken);
            registered++;
        }

        return registered;
    }

    private async Task RegisterAsync(Guid inputFileId, ClaimedFile claimed, CancellationToken cancellationToken)
    {
        var receivedAt = claimed.Info.ClaimedAtUtc;
        var manifest = ReadManifest(claimed.ManifestPath);
        var manifestJson = claimed.ManifestPath is not null && File.Exists(claimed.ManifestPath)
            ? await File.ReadAllTextAsync(claimed.ManifestPath, cancellationToken)
            : null;

        var fileInfo = new FileInfo(claimed.DataFilePath);
        var kind = FileKindOf(claimed.DataFilePath);
        var sha256 = await inbox.ComputeSha256Async(claimed.DataFilePath, cancellationToken);
        var originalName = SafeFileName(manifest?.OriginalFileName) ?? claimed.Info.OriginalFileName;

        var file = new InputFile
        {
            Id = inputFileId,
            UploadReference = Text.Truncate(manifest?.UploadReference, 40),
            Source = manifest is null ? InputFileSource.FolderDrop : InputFileSource.WebUpload,
            OriginalFileName = Text.Truncate(originalName, 260)!,
            FileKind = kind ?? FileKind.Txt,
            FileSizeBytes = fileInfo.Length,
            ContentSha256 = sha256,
            StoredFilePath = paths.ToRelative(claimed.DataFilePath),
            ManifestJson = manifestJson,
            CampaignId = manifest?.CampaignId,
            CampaignCode = Text.Truncate(manifest?.CampaignCode, 60),
            CampaignName = Text.Truncate(manifest?.CampaignName, 250),
            UploadedByUserId = manifest?.UploadedByUserId,
            UploadedByName = Text.Truncate(manifest?.UploadedByName, 200),
            ReceivedAtUtc = receivedAt,
        };

        // ---- Organisation -------------------------------------------------------------------------
        var tenant = await ResolveTenantAsync(manifest, claimed.Info.TenantFolder, cancellationToken);
        if (tenant.Error is not null)
        {
            await RejectAsync(file, claimed, tenant.Error, cancellationToken);
            return;
        }

        file.TenantId = tenant.Id;
        file.TenantCode = Text.Truncate(tenant.Code, 63);
        file.TenantName = Text.Truncate(tenant.Name, 200);

        // ---- Basic checks -----------------------------------------------------------------------------
        if (kind is null)
        {
            await RejectAsync(file, claimed, $"'{originalName}' is not a .txt, .csv, .xls or .xlsx file.", cancellationToken);
            return;
        }

        if (fileInfo.Length == 0)
        {
            await RejectAsync(file, claimed, $"'{originalName}' is empty.", cancellationToken);
            return;
        }

        var maxBytes = Settings.MaxFileSizeMegabytes * 1024L * 1024L;
        if (fileInfo.Length > maxBytes)
        {
            await RejectAsync(file, claimed,
                $"'{originalName}' is {fileInfo.Length / 1024d / 1024d:F1} MB; the limit is {Settings.MaxFileSizeMegabytes} MB.", cancellationToken);
            return;
        }

        if (manifest?.Sha256 is { Length: > 0 } expected && !string.Equals(expected, sha256, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(file, claimed,
                "The file content does not match the checksum recorded at upload (it was changed or damaged in transit).", cancellationToken);
            return;
        }

        // ---- Duplicate protection -------------------------------------------------------------------
        var options = manifest?.Options;
        var deliberateRerun = options is { ForceRecheck: true } or { AllowDuplicateFile: true };
        var dedupeKey = $"{file.TenantId:N}:{sha256}:{file.CampaignId?.ToString("N") ?? "none"}"
                        + (deliberateRerun ? $":{file.UploadReference ?? inputFileId.ToString("N")}" : string.Empty);

        if (await FindDuplicateAsync(dedupeKey, cancellationToken) is { } original)
        {
            await RecordDuplicateAsync(file, claimed, original, cancellationToken);
            return;
        }

        file.DedupeKey = dedupeKey;
        file.Status = InputFileStatus.Registered;
        file.RegisteredAtUtc = time.GetUtcNow();

        // ---- The job ------------------------------------------------------------------------------------
        var pipeline = pipelineOptions.Value;
        var job = new IntegrationJob
        {
            JobNumber = await references.NextAsync(ReferencePrefixes.IntegrationJob, cancellationToken),
            TenantId = file.TenantId,
            InputFileId = file.Id,
            CampaignId = file.CampaignId,
            Status = IntegrationJobStatus.Queued,
            CurrentStep = StepCodes.RegisterFile,
            RunCheckNumber = pipeline.EnableCheckNumber && (options?.RunCheckNumber ?? true),

            // A CHECK ONLY VERIFIES (15 October 2026). Its numbers are messaged when a person picks
            // them on the WhatsApp Messages page, never because a file arrived - so a job never asks
            // for WATI, whatever an older manifest or Pipeline:AutoSendWati still says, and the
            // manifest's per-upload template settings are not kept.
            RunWati = false,
            ForceRecheck = options?.ForceRecheck ?? false,
            CreatedByUserId = file.UploadedByUserId,
            CreatedByName = file.UploadedByName,
            CreatedAtUtc = file.RegisteredAtUtc.Value,
            UpdatedAtUtc = file.RegisteredAtUtc.Value,
            NextAttemptAtUtc = null,
        };

        db.InputFiles.Add(file);
        db.IntegrationJobs.Add(job);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Another instance registered the same content (or the same upload) a moment ago.
            db.ChangeTracker.Clear();
            if (await FindDuplicateAsync(dedupeKey, cancellationToken) is { } raced)
            {
                file.DedupeKey = null;
                await RecordDuplicateAsync(file, claimed, raced, cancellationToken);
                return;
            }

            throw;
        }

        await RecordDetectedAsync(file, claimed, cancellationToken);

        var message = $"Registered {file.OriginalFileName} ({file.FileSizeBytes:N0} bytes, {file.Source}) for {file.TenantName ?? file.TenantCode} "
                      + $"as job {job.JobNumber}" + (file.CampaignName is null ? "." : $" for campaign {file.CampaignName}.");
        logger.LogInformation("{Message}", message);

        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.FileRegistered,
            Message = message,
            TenantId = file.TenantId,
            InputFileId = file.Id,
            JobId = job.Id,
            StepCode = StepCodes.RegisterFile,
            Data = new { file.UploadReference, file.ContentSha256, job.RunCheckNumber, job.ForceRecheck },
        }, cancellationToken);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.JobCreated,
            Message = $"Job {job.JobNumber} queued (CheckNumber.ai {(job.RunCheckNumber ? "on" : "off")}; validation only - nothing is messaged until numbers are picked on the WhatsApp Messages page).",
            TenantId = file.TenantId,
            InputFileId = file.Id,
            JobId = job.Id,
            StepCode = StepCodes.RegisterFile,
        }, cancellationToken);
    }

    /// <summary>
    /// FILE_DETECTED, written once the int_input_files row exists (the event table references it),
    /// with the moment the file was claimed from the inbox.
    /// </summary>
    private Task RecordDetectedAsync(InputFile file, ClaimedFile claimed, CancellationToken cancellationToken) =>
        journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.FileDetected,
            Message = $"Detected {claimed.Info.InboxFileName} ({file.FileSizeBytes:N0} bytes) in the inbox"
                      + (claimed.Info.TenantFolder is null ? string.Empty : $" folder '{claimed.Info.TenantFolder}'")
                      + $" at {claimed.Info.ClaimedAtUtc:u}; claimed by {claimed.Info.WorkerInstance}.",
            TenantId = file.TenantId == Guid.Empty ? null : file.TenantId,
            InputFileId = file.Id,
            StepCode = StepCodes.RegisterFile,
            Data = new { claimed.Info.InboxFileName, claimed.Info.TenantFolder, claimed.Info.ClaimedAtUtc, file.Source },
        }, cancellationToken);

    private async Task<(Guid Id, string? JobNumber, DateTimeOffset? RegisteredAt)?> FindDuplicateAsync(string dedupeKey, CancellationToken cancellationToken)
    {
        var original = await db.InputFiles.AsNoTracking()
            .Where(f => f.DedupeKey == dedupeKey)
            .Select(f => new { f.Id, JobNumber = f.Job != null ? f.Job.JobNumber : null, f.RegisteredAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        return original is null ? null : (original.Id, original.JobNumber, original.RegisteredAtUtc);
    }

    private async Task RecordDuplicateAsync(
        InputFile file, ClaimedFile claimed, (Guid Id, string? JobNumber, DateTimeOffset? RegisteredAt) original, CancellationToken cancellationToken)
    {
        var reason = $"The same file was already processed for this organisation{(file.CampaignName is null ? string.Empty : " and campaign")} "
                     + $"as job {original.JobNumber ?? original.Id.ToString()} on {original.RegisteredAt:dd MMM yyyy HH:mm} UTC. "
                     + "No new job was created and nothing was sent to CheckNumber.ai. Upload it again with \"check again\" to run it anyway.";

        file.Status = InputFileStatus.Duplicate;
        file.DedupeKey = null;
        file.DuplicateOfInputFileId = original.Id;
        file.RejectionReason = reason;
        file.StoredFilePath = paths.ToRelative(inbox.MoveToRejected(claimed, paths.RejectedFolder(file.Id, file.ReceivedAtUtc), reason));
        file.RegisteredAtUtc = time.GetUtcNow();
        db.InputFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);

        await RecordDetectedAsync(file, claimed, cancellationToken);
        logger.LogWarning("{File}: duplicate of job {JobNumber}; not processed again.", file.OriginalFileName, original.JobNumber);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.FileDuplicate,
            Level = EventLevel.Warning,
            Message = $"{file.OriginalFileName}: {reason}",
            TenantId = file.TenantId,
            InputFileId = file.Id,
            StepCode = StepCodes.RegisterFile,
            Data = new { duplicateOf = original.Id, original.JobNumber, file.ContentSha256 },
        }, cancellationToken);
    }

    private async Task RejectAsync(InputFile file, ClaimedFile claimed, string reason, CancellationToken cancellationToken)
    {
        file.Status = InputFileStatus.Rejected;
        file.DedupeKey = null;
        file.RejectionReason = reason;
        file.StoredFilePath = paths.ToRelative(inbox.MoveToRejected(claimed, paths.RejectedFolder(file.Id, file.ReceivedAtUtc), reason));
        file.RegisteredAtUtc = time.GetUtcNow();
        db.InputFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);

        await RecordDetectedAsync(file, claimed, cancellationToken);
        logger.LogWarning("{File} was rejected: {Reason}", file.OriginalFileName, reason);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.FileRejected,
            Level = EventLevel.Warning,
            Message = $"{file.OriginalFileName} was rejected: {reason}",
            TenantId = file.TenantId == Guid.Empty ? null : file.TenantId,
            InputFileId = file.Id,
            StepCode = StepCodes.RegisterFile,
        }, cancellationToken);
    }

    /// <summary>The organisation: from the manifest, else the inbox sub-folder, else Worker:DefaultTenantSubdomain.</summary>
    private async Task<(Guid Id, string? Code, string? Name, string? Error)> ResolveTenantAsync(
        UploadManifest? manifest, string? tenantFolder, CancellationToken cancellationToken)
    {
        if (manifest is not null)
        {
            if (manifest.TenantId == Guid.Empty)
            {
                return (Guid.Empty, null, null, "The upload manifest does not name an organisation.");
            }

            var known = await TryFindTenantByIdAsync(manifest.TenantId, cancellationToken);
            return (manifest.TenantId, known?.Subdomain ?? manifest.TenantCode, known?.Name ?? manifest.TenantName, null);
        }

        var subdomain = tenantFolder ?? Blank(Settings.DefaultTenantSubdomain);
        if (subdomain is null)
        {
            return (Guid.Empty, null, null,
                "No organisation could be determined: put the file in inbox/{organisation-subdomain}/ or set Worker:DefaultTenantSubdomain.");
        }

        TenantInfo? tenant;
        try
        {
            tenant = await tenants.FindAsync(subdomain, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The organisation directory could not be read.");
            throw new IOException("The organisation directory (iam_tenants) could not be read.", exception);
        }

        return tenant is null
            ? (Guid.Empty, subdomain, null, $"No organisation has the subdomain or code '{subdomain}'.")
            : (tenant.Id, tenant.Subdomain, tenant.Name, null);
    }

    private async Task<TenantInfo?> TryFindTenantByIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            return await tenants.FindByIdAsync(tenantId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Organisation {TenantId} could not be looked up; using the manifest's values.", tenantId);
            return null;
        }
    }

    private UploadManifest? ReadManifest(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UploadManifest>(File.ReadAllText(path), JsonDefaults.Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning("The manifest {Path} could not be read: {Reason}", path, exception.Message);
            return null;
        }
    }

    private static FileKind? FileKindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".txt" => FileKind.Txt,
        ".csv" => FileKind.Csv,
        ".xls" => FileKind.Xls,
        ".xlsx" => FileKind.Xlsx,
        _ => null,
    };

    /// <summary>A file name safe to use on disk (no folders, no reserved characters), or null when there is none.</summary>
    public static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var justName = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*']).ToHashSet();
        var cleaned = new string(justName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimStart('.');
        return cleaned.Length == 0 ? null : cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is System.Data.Common.DbException db
        && (db.SqlState == "23505" || db.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
}
