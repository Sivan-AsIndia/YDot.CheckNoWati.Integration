using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
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

        return new
        {
            pipeline = new { p.EnableCheckNumber, p.EnableWati, p.AutoSendWati, p.AllowManualWatiBroadcasts },
            checkNumber = new
            {
                configured = !string.IsNullOrWhiteSpace(c.ApiKey),
                c.TaskType,
                c.InitialPollDelaySeconds,
                c.PollingIntervalSeconds,
                c.MaxPollingAttempts,
                c.MinimumNumbersPerTask,
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
                allowListActive = w.AllowedNumbers.Count > 0,
                allowListSize = w.AllowedNumbers.Count,
                w.SkipAlreadyMessagedNumbers,
                w.DuplicateWindowDays,
                w.SendToUnverifiedNumbers,
                statusSync = w.StatusSync.Enabled,
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

    /// <summary>"919597304379" -> "91XXXXXX4379": enough to recognise the sender number, not to copy it.</summary>
    private static string? Mask(string? number) =>
        string.IsNullOrWhiteSpace(number) || number.Length < 6
            ? number
            : number[..2] + new string('X', number.Length - 6) + number[^4..];
}
