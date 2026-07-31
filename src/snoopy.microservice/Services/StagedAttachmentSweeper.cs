namespace weesky.Snoopy.Microservice.Services;

/// <summary>Hourly GC over the staged store, so abandoned uploads never accumulate.</summary>
internal sealed class StagedAttachmentSweeper(
    IStagedAttachmentStore store,
    ILogger<StagedAttachmentSweeper> logger,
    TimeSpan? startupJitterMax = null) : BackgroundService
{
    // A restart drops the in-memory staged entries, orphaning every file already staged; without
    // a startup run the orphan sweep could only reclaim them an hour later. The jitter is short
    // because reclaiming disk promptly matters more here than for the trusted-sender sweep.
    private static readonly TimeSpan DefaultStartupJitterMax = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        var isFirstRun = true;

        while (isFirstRun || await timer.WaitForNextTickAsync(stoppingToken))
        {
            var runningStartupSweep = isFirstRun;
            isFirstRun = false;

            try
            {
                if (runningStartupSweep)
                {
                    await Task.Delay(RandomJitter(startupJitterMax ?? DefaultStartupJitterMax), stoppingToken);
                }

                // Every tick logs, zero included: the line is also the sweeper's heartbeat.
                var removed = store.SweepExpired();
                logger.LogInformation("Staged attachment sweep: {Count} file(s) removed", removed);
            }
            catch (Exception ex)
            {
                // A sweep that throws must not take the host down with it; the next tick retries.
                logger.LogError(ex, "The staged attachment sweep failed");
            }
        }
    }

    private static TimeSpan RandomJitter(TimeSpan max) =>
        TimeSpan.FromMilliseconds(Random.Shared.Next((int)Math.Max(0, max.TotalMilliseconds)));
}
