using Microsoft.Extensions.Options;
using YDot.Integration.Application.Diagnostics;
using YDot.Integration.Application.Options;

namespace YDot.Integration.Worker.Services;

/// <summary>Keeps this instance's int_worker_instances row fresh, and marks it Stopped on shutdown.</summary>
public sealed class HeartbeatWorker(
    IServiceScopeFactory scopes,
    IOptions<WorkerOptions> options,
    TimeProvider time,
    ILogger<HeartbeatWorker> logger) : PeriodicWorker(logger, time)
{
    protected override string Name => "Heartbeat";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.HeartbeatIntervalSeconds);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkerHeartbeat>().BeatAsync(stopping: false, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkerHeartbeat>().BeatAsync(stopping: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not mark this worker instance as stopped.");
        }
    }
}
