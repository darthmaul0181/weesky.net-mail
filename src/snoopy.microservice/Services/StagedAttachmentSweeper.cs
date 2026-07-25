namespace weesky.Snoopy.Microservice.Services;

/// <summary>Hourly GC over the staged store, so abandoned uploads never accumulate.</summary>
internal sealed class StagedAttachmentSweeper : BackgroundService
{
    private readonly IStagedAttachmentStore _store;
    private readonly ILogger<StagedAttachmentSweeper> _logger;

    public StagedAttachmentSweeper(IStagedAttachmentStore store, ILogger<StagedAttachmentSweeper> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Every tick logs, zero included: the line is also the sweeper's heartbeat.
                var removed = _store.SweepExpired();
                _logger.LogInformation("Staged attachment sweep: {Count} file(s) removed", removed);
            }
            catch (Exception ex)
            {
                // A sweep that throws must not take the host down with it; the next tick retries.
                _logger.LogError(ex, "The staged attachment sweep failed");
            }
        }
    }
}
