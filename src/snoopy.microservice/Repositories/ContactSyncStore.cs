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

    public async Task PlaceTombstoneAsync(
        Guid userId, string davName, ulong sequence, CancellationToken cancellationToken)
    {
        // Upsert and not insert: the key is (user_id, dav_name), so a name deleted, recreated and
        // deleted again lands on an existing row. A bare INSERT would fail that second deletion on
        // a duplicate key — in production, on data the user believes gone.
        var held = await context.ContactTombstones
            .SingleOrDefaultAsync(t => t.UserId == userId && t.DavName == davName, cancellationToken);

        if (held is null)
        {
            context.ContactTombstones.Add(new ContactTombstone
            {
                UserId = userId, DavName = davName, SyncSequence = sequence, DeletedAt = DateTime.UtcNow
            });
        }
        else
        {
            held.SyncSequence = sequence;
            held.DeletedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task LiftTombstoneAsync(Guid userId, string davName, CancellationToken cancellationToken)
    {
        var held = await context.ContactTombstones
            .SingleOrDefaultAsync(t => t.UserId == userId && t.DavName == davName, cancellationToken);
        if (held is null) return;

        context.ContactTombstones.Remove(held);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ArchiveAsync(ContactRevision revision, CancellationToken cancellationToken)
    {
        // The window guards one shape only: a client looping on a refusal. Two accepted writes that
        // land on the same hash are two facts, and dropping the second would lose an overwrite on
        // the table whose whole job is to lose nothing.
        if (revision.Cause == RevisionCause.Rejected)
        {
            var since = revision.ReplacedAt.AddHours(-24);
            var alreadyKept = await context.ContactRevisions.AnyAsync(
                r => r.UserId == revision.UserId
                     && r.DavName == revision.DavName
                     && r.CardHash == revision.CardHash
                     && r.Cause == revision.Cause
                     && r.ReplacedAt > since,
                cancellationToken);
            if (alreadyKept) return false;
        }

        context.ContactRevisions.Add(revision);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken)
    {
        var doomed = await context.ContactTombstones
            .Where(t => t.DeletedAt < tombstonesBefore)
            .ToListAsync(cancellationToken);

        // Source order here is not execution order: SaveChangesAsync batches and orders its own
        // commands, so writing the watermark update before the RemoveRange below does not, by
        // itself, make it commit first. What actually matters is that both go out under this one
        // SaveChangesAsync, hence one transaction — they commit together or not at all. That
        // atomicity is what makes an EF-chosen order inside the transaction safe. Split this into
        // two SaveChangesAsync calls and a process killed between them can leave the tombstones
        // gone and pruned_below behind: a stale token is then ACCEPTED, the response omits the
        // deletion with nothing to signal it, and the client keeps the card for ever.
        var highest = doomed
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.SyncSequence));

        foreach (var (userId, sequence) in highest)
        {
            var state = await context.ContactSyncStates
                .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);
            if (state is null) continue;

            // Never downwards. It is also what makes the sweep safe on several instances at once:
            // the write is commutative, and a DELETE that no longer finds its rows removes zero.
            state.PrunedBelow = Math.Max(state.PrunedBelow, sequence);
        }

        context.ContactTombstones.RemoveRange(doomed);

        var staleRevisions = await context.ContactRevisions
            .Where(r => r.ReplacedAt < revisionsBefore)
            .ToListAsync(cancellationToken);
        context.ContactRevisions.RemoveRange(staleRevisions);

        // One SaveChanges, so the watermark and the removal commit together or not at all.
        await context.SaveChangesAsync(cancellationToken);

        return new PruneOutcome(doomed.Count, staleRevisions.Count);
    }
}
