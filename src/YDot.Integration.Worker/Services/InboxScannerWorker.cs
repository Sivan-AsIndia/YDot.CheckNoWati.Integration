using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Inbox;
using YDot.Integration.Application.Options;

namespace YDot.Integration.Worker.Services;

/// <summary>
/// Watches the inbox folder (Worker:DataRoot/inbox) and registers every ready file as an input
/// file + queued job. Polling rather than FileSystemWatcher on purpose: watcher events are lost on
/// network shares and Docker volumes, a scan never is.
/// </summary>
public sealed class InboxScannerWorker(
    IServiceScopeFactory scopes,
    IDataPaths paths,
    IOptions<WorkerOptions> options,
    TimeProvider time,
    ILogger<InboxScannerWorker> logger) : PeriodicWorker(logger, time)
{
    private bool announced;

    protected override string Name => "Inbox scanner";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.ScanIntervalSeconds);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!announced)
        {
            paths.EnsureCreated();
            logger.LogInformation("Drop number files (.txt, .csv, .xls, .xlsx) into {Inbox} - or into {Inbox}/{{organisation-subdomain}}/.",
                paths.Inbox, paths.Inbox);
            announced = true;
        }

        await using var scope = scopes.CreateAsyncScope();
        var registered = await scope.ServiceProvider.GetRequiredService<InboxRegistrationService>().ScanAsync(cancellationToken);

        if (registered > 0)
        {
            logger.LogInformation("Inbox scan registered {Count} file(s).", registered);
        }
    }
}
