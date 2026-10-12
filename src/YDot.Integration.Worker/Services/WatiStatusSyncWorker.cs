using Microsoft.Extensions.Options;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Wati;

namespace YDot.Integration.Worker.Services;

/// <summary>Follows accepted WATI messages to Sent / Delivered / Read / Failed (Wati:StatusSync).</summary>
public sealed class WatiStatusSyncWorker(
    IServiceScopeFactory scopes,
    IOptions<WatiOptions> wati,
    IOptions<PipelineOptions> pipeline,
    TimeProvider time,
    ILogger<WatiStatusSyncWorker> logger) : PeriodicWorker(logger, time)
{
    protected override string Name => "WATI status sync";

    protected override bool Enabled => pipeline.Value.EnableWati && wati.Value.StatusSync.Enabled;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(wati.Value.StatusSync.IntervalSeconds);

    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(15);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WatiStatusSynchronizer>().SyncDueAsync(cancellationToken);
    }
}
