namespace weesky.Snoopy.Microservice.Services;

/// <summary>Hourly GC over the staged store, so abandoned uploads never accumulate.</summary>
internal sealed class StagedAttachmentSweeper(
    IStagedAttachmentStore store,
    ILogger<StagedAttachmentSweeper> logger,
    TimeSpan? startupJitterMax = null)
    : PeriodicSweeper(TimeSpan.FromHours(1), startupJitterMax ?? DefaultStartupJitterMax, logger)
{
    // A restart drops the in-memory staged entries, orphaning every file already staged; without
    // a startup run the orphan sweep could only reclaim them an hour later. The jitter is short
    // because reclaiming disk promptly matters more here than for the trusted-sender sweep.
    private static readonly TimeSpan DefaultStartupJitterMax = TimeSpan.FromSeconds(5);

    protected override string SweepName => "staged attachment";

    protected internal override Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        // Every tick logs, zero included: the line is also the sweeper's heartbeat.
        var removed = store.SweepExpired();
        logger.LogInformation("Staged attachment sweep: {Count} file(s) removed", removed);

        return Task.CompletedTask;
    }
}
