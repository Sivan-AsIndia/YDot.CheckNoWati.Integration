using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Pipeline;

namespace YDot.Integration.Worker.Services;

/// <summary>
/// Keeps up to Worker:MaxConcurrentJobs integration jobs moving at once. Each round it claims as
/// many due jobs as it has free slots (leases make the claim safe across instances) and hands each
/// one to the <see cref="JobProcessor"/>, which runs stages until the job waits or finishes.
/// </summary>
public sealed class JobProcessorWorker(
    ILeaseStore leases,
    JobProcessor processor,
    IOptions<WorkerOptions> options,
    TimeProvider time,
    ILogger<JobProcessorWorker> logger) : BackgroundService
{
    private readonly List<Task> running = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation("Job processor started: up to {Max} job(s) at a time, checking every {Interval}s.",
            settings.MaxConcurrentJobs, settings.JobPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                running.RemoveAll(task => task.IsCompleted);
                var free = settings.MaxConcurrentJobs - running.Count;

                if (free > 0)
                {
                    foreach (var jobId in await leases.ClaimIntegrationJobsAsync(free, stoppingToken))
                    {
                        running.Add(Task.Run(() => processor.ProcessAsync(jobId, stoppingToken), CancellationToken.None));
                    }
                }

                var tick = Task.Delay(TimeSpan.FromSeconds(settings.JobPollIntervalSeconds), time, stoppingToken);
                await (running.Count == 0 ? tick : Task.WhenAny(running.Append(tick)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Job processor round failed; trying again shortly.");
                await SafeDelay(TimeSpan.FromSeconds(settings.JobPollIntervalSeconds), stoppingToken);
            }
        }

        // Let jobs in flight reach a safe point (they observe the same token) and release their leases.
        await Task.WhenAll(running.Select(task => task.ContinueWith(_ => { }, TaskScheduler.Default)));
        logger.LogInformation("Job processor stopped.");
    }

    private async Task SafeDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, time, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
