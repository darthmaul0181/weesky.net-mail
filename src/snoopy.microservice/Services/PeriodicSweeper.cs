namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The shape both background sweeps share: one pass at startup, then one per period, and a failure
/// that is logged rather than allowed to take the host down with it.
///
/// The startup pass is load-bearing and was the reason both sweepers grew this loop independently:
/// every push restarts the process, so a sweeper that only ran on the timer's tick never ran at all
/// on a service redeployed more often than its own period. The jitter staggers a restart storm.
/// </summary>
internal abstract class PeriodicSweeper(TimeSpan period, TimeSpan startupJitterMax, ILogger logger)
    : BackgroundService
{
    /// <summary>Names this sweep in the failure log.</summary>
    protected abstract string SweepName { get; }

    /// <summary>
    /// One pass. Logs its own outcome — every tick, zero included, so the line doubles as the
    /// sweeper's heartbeat — and lets anything it cannot handle throw: the loop survives it.
    /// </summary>
    protected internal abstract Task SweepOnceAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(period);
        var isFirstRun = true;

        while (isFirstRun || await timer.WaitForNextTickAsync(stoppingToken))
        {
            var runningStartupSweep = isFirstRun;
            isFirstRun = false;

            try
            {
                if (runningStartupSweep) await Task.Delay(RandomJitter(startupJitterMax), stoppingToken);

                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A sweep that throws must not take the host down with it; the next tick retries.
                logger.LogError(ex, "The {SweepName} sweep failed", SweepName);
            }
        }
    }

    private static TimeSpan RandomJitter(TimeSpan max) =>
        TimeSpan.FromMilliseconds(Random.Shared.Next((int)Math.Max(0, max.TotalMilliseconds)));
}
