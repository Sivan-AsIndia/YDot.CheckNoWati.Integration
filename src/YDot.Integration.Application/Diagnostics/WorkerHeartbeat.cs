using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Commands;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Application.Diagnostics;

/// <summary>
/// Writes this instance's int_worker_instances row: alive-time, version, and the effective
/// settings WITHOUT secrets. The web application reads the newest row to show whether the worker
/// is running, the price used for estimates, the WATI safety caps and the pipeline switches.
/// </summary>
public sealed class WorkerHeartbeat(
    IIntegrationDbContext db,
    IWorkerIdentity identity,
    IHostEnvironment environment,
    IOptions<PipelineOptions> pipeline,
    IOptions<CheckNumberOptions> checkNumber,
    IOptions<WatiOptions> wati,
    IOptions<WorkerOptions> worker,
    IOptions<RetryOptions> retry,
    TimeProvider time)
{
    public async Task BeatAsync(bool stopping, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var row = await db.WorkerInstances.SingleOrDefaultAsync(w => w.Id == identity.InstanceId, cancellationToken);

        if (row is null)
        {
            row = new WorkerInstance
            {
                Id = identity.InstanceId,
                InstanceName = identity.InstanceName,
                HostName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                Version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                EnvironmentName = environment.EnvironmentName,
                StartedAtUtc = identity.StartedAtUtc,
            };
            db.WorkerInstances.Add(row);
        }

        row.Status = stopping ? "Stopped" : "Running";
        row.LastHeartbeatAtUtc = now;
        row.StoppedAtUtc = stopping ? now : null;
        row.SettingsJson = JsonDefaults.Serialize(PublicSettings());
        await db.SaveChangesAsync(cancellationToken);

        // AN INSTANCE THAT WAS KILLED NEVER GETS TO SAY IT STOPPED - a power cut, a removed
        // container, taskkill. Its row would read "Running" for ever. Whoever is still alive marks
        // it "Lost" once it has been silent for ten heartbeats (five minutes at the least), stamped
        // with the last moment it was actually heard from. Its jobs need no such help: their leases
        // expire by themselves and another instance picks them up.
        var silentSince = now.AddSeconds(-Math.Max(300, worker.Value.HeartbeatIntervalSeconds * 10));
        await db.WorkerInstances
            .Where(w => w.Id != identity.InstanceId && w.Status == "Running" && w.LastHeartbeatAtUtc < silentSince)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(w => w.Status, "Lost")
                    .SetProperty(w => w.StoppedAtUtc, w => w.LastHeartbeatAtUtc),
                cancellationToken);
    }

    private object PublicSettings()
    {
        var c = checkNumber.Value;
        var w = wati.Value;
        var p = pipeline.Value;
        // As the recipient planner reads the allow-list: digits only, blanks dropped.
        var allowList = w.EffectiveAllowedNumbers()
            .Select(PhoneNumbers.PhoneNumberNormalizer.DigitsOnly)
            .Where(digits => digits.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new
        {
            pipeline = new
            {
                p.EnableCheckNumber,
                p.EnableWati,

                // Retired: a check never messages anybody, whatever the setting says.
                autoSendWati = false,
                p.AllowManualWatiBroadcasts,
                sendsRequireSelection = true,
            },
            checkNumber = new
            {
                configured = !string.IsNullOrWhiteSpace(c.ApiKey),
                c.TaskType,
                c.InitialPollDelaySeconds,
                c.PollingIntervalSeconds,
                c.MaxPollingAttempts,
                c.MinimumNumbersPerTask,

                // How many numbers the minimum charge pays for: CheckNumber.ai bills a 2-number task
                // like a 500-number one, so a check should hold at least this many to get full value.
                billableMinimumNumbers = c.PricePerNumberUsd > 0
                    ? (int)Math.Ceiling(c.MinimumChargePerTaskUsd / c.PricePerNumberUsd)
                    : 0,
                c.MaxNumbersPerTask,
                c.SplitTasksByCountry,
                reuseScope = c.ReuseScope.ToString(),
                c.ResultValidityDays,
                c.PricePerNumberUsd,
                c.MinimumChargePerTaskUsd,
                currency = "USD",
            },
            wati = new
            {
                configured = w.IsConfigured,
                channelNumber = Mask(w.ChannelNumber),
                w.BatchSize,
                w.DefaultTemplateName,
                w.DefaultBroadcastName,
                defaultParameters = w.DefaultParameters.Select(x => new { x.Name, x.Value }),
                w.MaxRecipientsPerJob,
                allowListActive = allowList.Count > 0,
                allowListSize = allowList.Count,

                // A fingerprint of each allow-listed number (SHA-256 of its digits, lower-case hex), so the
                // web application's review can mark the selected numbers the allow-list will hold back
                // (NOT_IN_ALLOWLIST) without the numbers themselves being published.
                allowListSha256 = allowList.Select(Fingerprint),
                w.SkipAlreadyMessagedNumbers,
                w.DuplicateWindowDays,
                w.SendToUnverifiedNumbers,
                statusSync = w.StatusSync.Enabled,

                // The failure codes "retry this send" puts back in the queue, so the web application
                // offers Retry only where the worker would act on it.
                retryableFailureCodes = WorkerCommandProcessor.RetryableWatiFailures,
            },
            worker = new
            {
                worker.Value.MaxFileSizeMegabytes,
                worker.Value.ScanIntervalSeconds,
                worker.Value.AcceptFolderDrops,
                worker.Value.MaxConcurrentJobs,
            },
            retry = new { retry.Value.MaxAttempts, retry.Value.InitialDelaySeconds, retry.Value.MaxDelaySeconds },
        };
    }

    private static string Fingerprint(string digits) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(digits))).ToLowerInvariant();

    /// <summary>"919597304379" -> "91XXXXXX4379": enough to recognise the sender number, not to copy it.</summary>
    private static string? Mask(string? number) =>
        string.IsNullOrWhiteSpace(number) || number.Length < 6
            ? number
            : number[..2] + new string('X', number.Length - 6) + number[^4..];
}
