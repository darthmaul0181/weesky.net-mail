using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Daily GC over calendar tombstones and revisions: <see cref="ICalendarSyncStore.PruneAsync"/>
/// raises each collection's watermark and removes what it now covers, tombstones at 180 days,
/// revisions at 30 — past that a deleted event stays correctly deleted everywhere, it is simply no
/// longer restorable.
///
/// <b>Two instances of this sweeper must never run concurrently</b>, for the reason
/// <see cref="ContactTombstoneSweeper"/> spells out: two overlapping doomed sets collide, the
/// loser's <c>RemoveRange</c> raises <c>DbUpdateConcurrencyException</c> and rolls its whole
/// transaction back, watermark included. Nothing is lost, the work is.
/// </summary>
internal sealed class CalendarTombstoneSweeper(
    IServiceScopeFactory scopes,
    ILogger<CalendarTombstoneSweeper> logger,
    TimeSpan? startupJitterMax = null)
    : PeriodicSweeper(TimeSpan.FromDays(1), startupJitterMax ?? DefaultStartupJitterMax, logger)
{
    // Five minutes, as the contacts sweep takes: a daily pass over two tables, staggered further
    // apart from the other sweepers' own startup jitter.
    private static readonly TimeSpan DefaultStartupJitterMax = TimeSpan.FromMinutes(5);

    protected override string SweepName => "calendar tombstone";

    /// <summary>
    /// One pass. Opens a scope of its own because the store and its DbContext are scoped while this
    /// service is a singleton — injecting the store directly compiles and throws here.
    /// </summary>
    protected internal override async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICalendarSyncStore>();

        var outcome = await store.PruneAsync(
            DateTime.UtcNow.AddDays(-180), DateTime.UtcNow.AddDays(-30), cancellationToken);

        // Every tick logs, zero included: the line is also the sweeper's heartbeat.
        logger.LogInformation(
            "Calendar tombstone sweep: {TombstoneCount} tombstone(s), {RevisionCount} revision(s) " +
            "removed, capped={Capped}",
            outcome.Tombstones, outcome.Revisions, outcome.Capped);

        // Said out loud and separately, because the line above reads as "everything old is gone"
        // when it is not: several capped ticks in a row is a deployment whose sweeper has not run.
        if (outcome.Capped)
        {
            logger.LogWarning(
                "The calendar tombstone sweep reached its {Cap}-row ceiling; older rows remain for " +
                "the next pass", CalendarSyncStore.MaxRowsPerSweep);
        }
    }
}
