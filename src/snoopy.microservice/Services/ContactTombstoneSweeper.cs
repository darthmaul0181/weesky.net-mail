using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Daily GC over contact tombstones and revisions: <see cref="IContactSyncStore.PruneAsync"/>
/// raises the watermark and removes what it now covers, tombstones at 180 days, revisions at 30 —
/// past that a deleted card stays correctly deleted everywhere, it is simply no longer restorable.
///
/// <b>Two instances of this sweeper must never run concurrently.</b> <c>PruneAsync</c> reads the
/// doomed tombstone set and raises the watermark inside one transaction, so two concurrent sweeps
/// reading overlapping doomed sets collide: the second to commit finds its rows already gone,
/// <c>RemoveRange</c> raises <c>DbUpdateConcurrencyException</c>, and its whole transaction rolls
/// back, watermark included. Nothing is lost, but the work is — this is a fault to avoid, not a
/// supported mode, and it is why this service must run as a singleton on exactly one instance.
///
/// This is also the first periodic reader of <c>contact_revisions</c>. <c>PruneAsync</c>
/// materialises <see cref="Data.Preferences.ContactRevision"/> entities, and the <c>Cause</c>
/// column's value converter runs <c>Enum.Parse&lt;RevisionCause&gt;(v, true)</c> on every row it
/// touches — which throws on any value outside the five defined names. MySQL's <c>ENUM</c>
/// constrains writes, but a non-strict-mode insert can still store <c>''</c>, and one such row
/// would make every sweep fail forever after. The fix is not to loosen the converter — a loud
/// failure here is the right choice — but to find and correct that row's <c>cause</c> directly in
/// the database; this sweep's error log line, repeated on every failed tick by
/// <see cref="PeriodicSweeper"/>, is where an operator will meet it first.
/// </summary>
internal sealed class ContactTombstoneSweeper(
    IServiceScopeFactory scopes,
    ILogger<ContactTombstoneSweeper> logger,
    TimeSpan? startupJitterMax = null)
    : PeriodicSweeper(TimeSpan.FromDays(1), startupJitterMax ?? DefaultStartupJitterMax, logger)
{
    // Five minutes, not TrustedSenderSweeper's thirty seconds: this is a daily pass over two
    // tables, deliberately staggered further apart from the other sweepers' own startup jitter.
    private static readonly TimeSpan DefaultStartupJitterMax = TimeSpan.FromMinutes(5);

    protected override string SweepName => "contact tombstone";

    /// <summary>
    /// One pass. Opens a scope of its own because the store and its DbContext are scoped while
    /// this service is a singleton — injecting the store directly compiles and throws here.
    /// </summary>
    protected internal override async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IContactSyncStore>();

        var outcome = await store.PruneAsync(
            DateTime.UtcNow.AddDays(-180), DateTime.UtcNow.AddDays(-30), cancellationToken);

        // Every tick logs, zero included: the line is also the sweeper's heartbeat.
        logger.LogInformation(
            "Contact tombstone sweep: {TombstoneCount} tombstone(s), {RevisionCount} revision(s) removed",
            outcome.Tombstones, outcome.Revisions);
    }
}
