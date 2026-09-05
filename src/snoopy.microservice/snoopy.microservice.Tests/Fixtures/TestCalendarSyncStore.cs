using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Tests.Fixtures;

/// <summary>
/// The real <see cref="CalendarSyncStore"/> with one method stood in for:
/// <see cref="NextSequenceAsync"/> is <c>INSERT … ON DUPLICATE KEY UPDATE seq = seq + 1</c>, MySQL
/// syntax the InMemory provider cannot execute and whose transaction it ignores anyway. The EF
/// increment below honours the same contract — one rank per call, monotone per collection, first
/// rank 1 — so everything built on it is tested against the behaviour production gives.
/// Its atomicity under concurrency is verified by hand against MariaDB, and nowhere else.
/// </summary>
internal sealed class TestCalendarSyncStore(PreferencesDbContext context) : ICalendarSyncStore
{
    private readonly CalendarSyncStore inner = new(context);

    /// <summary>How many ranks this store handed out — what a test asserting "three batches" reads.</summary>
    internal int RankCalls { get; private set; }

    public async Task<ulong> NextSequenceAsync(Guid calendarId, CancellationToken cancellationToken)
    {
        RankCalls++;

        var state = await context.CalendarSyncStates
            .FirstOrDefaultAsync(s => s.CalendarId == calendarId, cancellationToken);
        if (state is null)
        {
            state = new CalendarSyncState
            {
                CalendarId = calendarId, Epoch = Guid.NewGuid(), Seq = 0, PrunedBelow = 0
            };
            context.CalendarSyncStates.Add(state);
        }

        state.Seq += 1;
        await context.SaveChangesAsync(cancellationToken);
        return state.Seq;
    }

    public Task CreateStateAsync(Guid calendarId, CancellationToken cancellationToken) =>
        inner.CreateStateAsync(calendarId, cancellationToken);

    public Task<SyncState?> ReadStateAsync(Guid calendarId, CancellationToken cancellationToken) =>
        inner.ReadStateAsync(calendarId, cancellationToken);

    public Task PlaceTombstoneAsync(
        Guid calendarId, string davName, ulong rank, CancellationToken cancellationToken) =>
        inner.PlaceTombstoneAsync(calendarId, davName, rank, cancellationToken);

    public Task LiftTombstoneAsync(
        Guid calendarId, string davName, CancellationToken cancellationToken) =>
        inner.LiftTombstoneAsync(calendarId, davName, cancellationToken);

    public Task ArchiveAsync(
        Guid userId, Guid? calendarId, Guid? eventId, string? uid, string? davName, string icsRaw,
        RevisionCause cause, CancellationToken cancellationToken) =>
        inner.ArchiveAsync(
            userId, calendarId, eventId, uid, davName, icsRaw, cause, cancellationToken);

    public Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken) =>
        inner.PruneAsync(tombstonesBefore, revisionsBefore, cancellationToken);
}
