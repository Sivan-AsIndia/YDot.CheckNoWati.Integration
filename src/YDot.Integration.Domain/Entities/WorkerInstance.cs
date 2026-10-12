using YDot.Integration.Domain.Common;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// A running (or stopped) copy of the worker (table <c>int_worker_instances</c>), refreshed by a
/// heartbeat. The web application reads it to tell people whether files are being processed,
/// and <see cref="SettingsJson"/> publishes the effective, non-secret settings (pipeline
/// switches, pricing, batch size, safety caps) so the screens can show them without keeping a
/// second copy of the configuration.
/// </summary>
public sealed class WorkerInstance : Entity
{
    /// <summary>Configured name, or machine name + process id.</summary>
    public string InstanceName { get; set; } = string.Empty;

    public string HostName { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public string Version { get; set; } = string.Empty;

    public string EnvironmentName { get; set; } = string.Empty;

    /// <summary>Running or Stopped.</summary>
    public string Status { get; set; } = "Running";

    /// <summary>Effective configuration without secrets (jsonb).</summary>
    public string? SettingsJson { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset LastHeartbeatAtUtc { get; set; }

    public DateTimeOffset? StoppedAtUtc { get; set; }
}
