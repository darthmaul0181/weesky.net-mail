using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// The real <see cref="ContactSyncStore"/> for everything but <see cref="NextSequenceAsync"/>,
/// whose raw SQL and owned-transaction guard the InMemory provider cannot honour: the rank is
/// advanced on the state row directly. This keeps a controller test's archives and ranks real
/// rows the test reads back off the database — "a refused PUT takes no rank" stays a claim about
/// stored state, not about a mock — while the counter's locking remains the production store's
/// own, hand-verified property.
/// </summary>
internal sealed class InMemorySyncStore(PreferencesDbContext context) : IContactSyncStore
{
    private readonly ContactSyncStore inner = new(context);

    public async Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var state = await context.ContactSyncStates
            .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (state is null)
        {
            state = new ContactSyncState { UserId = userId, Epoch = Guid.NewGuid() };
            context.ContactSyncStates.Add(state);
        }

        state.Seq++;
        await context.SaveChangesAsync(cancellationToken);
        return state.Seq;
    }

    public Task<SyncState?> ReadStateAsync(Guid userId, CancellationToken cancellationToken) =>
        inner.ReadStateAsync(userId, cancellationToken);

    public Task<SyncState> ReadOrCreateStateAsync(Guid userId, CancellationToken cancellationToken) =>
        inner.ReadOrCreateStateAsync(userId, cancellationToken);

    public Task PlaceTombstoneAsync(
        Guid userId, string davName, ulong sequence, CancellationToken cancellationToken) =>
        inner.PlaceTombstoneAsync(userId, davName, sequence, cancellationToken);

    public Task LiftTombstoneAsync(Guid userId, string davName, CancellationToken cancellationToken) =>
        inner.LiftTombstoneAsync(userId, davName, cancellationToken);

    public Task<bool> ArchiveAsync(ContactRevision revision, CancellationToken cancellationToken) =>
        inner.ArchiveAsync(revision, cancellationToken);

    public Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken) =>
        inner.PruneAsync(tombstonesBefore, revisionsBefore, cancellationToken);
}
