using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Wati;

namespace YDot.Integration.Worker.Services;

/// <summary>
/// Sends queued WATI jobs - the sends a person requests for a selection of numbers on the WhatsApp
/// Messages page (a check never queues one any more; an older automatic job still in the queue is
/// finished the same way) - one job at a time per instance, batch by batch.
/// </summary>
public sealed class WatiDispatchWorker(
    IServiceScopeFactory scopes,
    ILeaseStore leases,
    IOptions<PipelineOptions> pipeline,
    IOptions<WorkerOptions> worker,
    TimeProvider time,
    ILogger<WatiDispatchWorker> logger) : PeriodicWorker(logger, time)
{
    protected override string Name => "WATI dispatcher";

    protected override bool Enabled => pipeline.Value.EnableWati;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(worker.Value.JobPollIntervalSeconds);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var watiJobId in await leases.ClaimWatiJobsAsync(1, cancellationToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WatiJobSender>().SendAsync(watiJobId, cancellationToken);
            }
            finally
            {
                await leases.ReleaseWatiJobAsync(watiJobId, CancellationToken.None);
            }
        }
    }
}
