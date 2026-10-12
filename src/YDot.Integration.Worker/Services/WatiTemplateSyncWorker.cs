using Microsoft.Extensions.Options;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Wati;

namespace YDot.Integration.Worker.Services;

/// <summary>Copies WATI's templates into int_wati_templates at start-up and every Wati:TemplateSync:IntervalMinutes.</summary>
public sealed class WatiTemplateSyncWorker(
    IServiceScopeFactory scopes,
    IOptions<WatiOptions> wati,
    TimeProvider time,
    ILogger<WatiTemplateSyncWorker> logger) : PeriodicWorker(logger, time)
{
    protected override string Name => "WATI template sync";

    protected override bool Enabled => wati.Value.TemplateSync.Enabled;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(wati.Value.TemplateSync.IntervalMinutes);

    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(5);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WatiTemplateSynchronizer>().SyncAsync(cancellationToken);
    }
}
