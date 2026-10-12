using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Commands;
using YDot.Integration.Application.Options;

namespace YDot.Integration.Worker.Services;

/// <summary>Carries out the requests the web application leaves in int_worker_commands.</summary>
public sealed class CommandWorker(
    IServiceScopeFactory scopes,
    ILeaseStore leases,
    IOptions<WorkerOptions> options,
    TimeProvider time,
    ILogger<CommandWorker> logger) : PeriodicWorker(logger, time)
{
    protected override string Name => "Command processor";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.CommandPollIntervalSeconds);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var commandId in await leases.ClaimCommandsAsync(5, cancellationToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkerCommandProcessor>().ProcessAsync(commandId, cancellationToken);
        }
    }
}
