namespace YDot.Integration.Worker.Services;

/// <summary>
/// Base for the background services: run one round, wait, repeat - until the host stops.
///
/// A round that throws is logged and the loop carries on after the normal interval, so a
/// temporary problem (database restarting, network blip) never kills a background service.
/// </summary>
public abstract class PeriodicWorker(ILogger logger, TimeProvider time) : BackgroundService
{
    /// <summary>Time between two rounds.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>Wait before the first round (lets the other services start first).</summary>
    protected virtual TimeSpan InitialDelay => TimeSpan.Zero;

    /// <summary>False switches the service off (it logs that once and returns).</summary>
    protected virtual bool Enabled => true;

    protected abstract string Name { get; }

    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            logger.LogInformation("{Worker} is switched off by configuration.", Name);
            return;
        }

        logger.LogInformation("{Worker} started (every {Interval}).", Name, Interval);

        try
        {
            if (InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(InitialDelay, time, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "{Worker} round failed; trying again in {Interval}.", Name, Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        logger.LogInformation("{Worker} stopped.", Name);
    }
}
