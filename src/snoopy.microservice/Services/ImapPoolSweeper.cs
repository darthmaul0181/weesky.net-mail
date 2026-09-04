namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Closes pooled IMAP sockets nobody will borrow again — without it, a socket whose tab was closed
/// would live until the next borrow, which is never. At 15 s a line per tick is 5,760 a day, so the
/// heartbeat is one aggregate every <see cref="PassesPerReport"/> passes.
/// </summary>
internal sealed class ImapPoolSweeper(
    IImapConnectionPool pool,
    ILogger<ImapPoolSweeper> logger,
    TimeSpan? period = null) : BackgroundService
{
    internal static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(15);

    /// <summary>Five minutes at the default period.</summary>
    internal const int PassesPerReport = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(period ?? DefaultPeriod);
        var passes = 0;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await pool.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The IMAP pool sweep failed");
            }

            if (++passes % PassesPerReport == 0) Report();
        }
    }

    private void Report()
    {
        var s = pool.Snapshot();
        logger.LogInformation(
            "IMAP pool: {Idle} idle, {Borrowed} borrowed, {Keys} keys; {Borrows} borrows, {Reused} reused, " +
            "{Opened} opened, {SingleUse} single-use, {HealthFailures} health failures, {ClosedIdle} closed idle, " +
            "{ClosedLifetime} closed lifetime, {ClosedAtReturn} closed at return, {Evicted} evicted",
            s.Idle, s.Borrowed, s.Keys, s.Borrows, s.Reused, s.Opened, s.SingleUse, s.HealthFailures,
            s.ClosedIdle, s.ClosedLifetime, s.ClosedAtReturn, s.Evicted);
    }
}
