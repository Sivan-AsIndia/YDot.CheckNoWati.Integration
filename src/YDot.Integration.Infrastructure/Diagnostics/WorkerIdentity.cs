using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Options;

namespace YDot.Integration.Infrastructure.Diagnostics;

/// <summary>
/// This process's identity. The name includes the process id by default, so two copies started on
/// the same machine (Visual Studio and a console, say) are two distinct lease holders.
/// </summary>
internal sealed class WorkerIdentity(IOptions<WorkerOptions> options, TimeProvider time) : IWorkerIdentity
{
    public Guid InstanceId { get; } = Guid.CreateVersion7();

    public string InstanceName { get; } = string.IsNullOrWhiteSpace(options.Value.InstanceName)
        ? $"{Environment.MachineName}-{Environment.ProcessId}"
        : $"{options.Value.InstanceName.Trim()}-{Environment.ProcessId}";

    public DateTimeOffset StartedAtUtc { get; } = time.GetUtcNow();
}
