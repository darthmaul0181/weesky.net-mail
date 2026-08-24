using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IContactSyncStore"/>
internal sealed class ContactSyncStore(PreferencesDbContext context) : IContactSyncStore
{
    public async Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Raw SQL, and the only raw SQL in the repository. Three things it must do in one
        // statement: create the row when the user has none, take the row's exclusive lock, and
        // advance the counter — all inside the caller's transaction, so InnoDB holds that lock
        // until COMMIT and a second writer cannot get its rank before the first is visible.
        // Splitting them — take a number, then write in another transaction — reopens the hole from
        // the other end: rank 11 committed before rank 10, and a client syncing in between takes
        // token 11 and never sees 10.
        //
        // The Guid.NewGuid() below is drawn on every call, including when the row already exists —
        // without consequence: ON DUPLICATE KEY UPDATE touches only seq, so the epoch already
        // stored is never rewritten. Worth stating, because a reviewer seeing a fresh GUID on every
        // increment will suspect exactly the opposite, and a moving epoch is precisely the defect
        // that would invalidate every token on every write.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO contact_sync_state (user_id, epoch, seq, pruned_below)
             VALUES ({userId}, {Guid.NewGuid()}, 1, 0)
             ON DUPLICATE KEY UPDATE seq = seq + 1
             """,
            cancellationToken);

        // Re-read inside the same transaction: the statement above cannot return the new value, and
        // LAST_INSERT_ID() would answer the auto-increment of a table that has none.
        var seq = await context.ContactSyncStates
            .Where(s => s.UserId == userId)
            .Select(s => s.Seq)
            .SingleAsync(cancellationToken);

        return seq;
    }

    public async Task<SyncState?> ReadStateAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.ContactSyncStates
            .Where(s => s.UserId == userId)
            .Select(s => new SyncState(s.Epoch, s.Seq, s.PrunedBelow))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SyncState> ReadOrCreateStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var held = await ReadStateAsync(userId, cancellationToken);
        if (held is not null) return held;

        var created = new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = 0, PrunedBelow = 0
        };
        context.ContactSyncStates.Add(created);
        await context.SaveChangesAsync(cancellationToken);

        return new SyncState(created.Epoch, 0, 0);
    }
}
