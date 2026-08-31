using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IContactSyncStore"/>
internal sealed class ContactSyncStore(PreferencesDbContext context) : IContactSyncStore
{
    /// <summary>
    /// What one <see cref="PruneAsync"/> pass may remove from each of its two tables. The sweep is
    /// unfiltered across the deployment and runs daily, so this is not a policy about what to keep
    /// — everything past the cap is taken by the next pass — but the bound that keeps one pass's
    /// footprint independent of how large a backlog grew while the sweeper was not running.
    /// </summary>
    internal const int MaxRowsPerSweep = 50_000;

    public async Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The one precondition prose alone cannot enforce: outside a transaction ExecuteSql* runs
        // in autocommit, the row's lock drops the instant the statement completes, and EF may hand
        // the connection back to the pool before the re-read below runs — which can then land on a
        // different physical connection and answer a rank two callers both hold. The InMemory
        // provider never opens a transaction, so this guard is what makes a caller in tasks 5-9 who
        // forgot to open one fail loudly here instead of silently in production.
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(NextSequenceAsync)} must run inside a transaction the caller owns.");
        }

        // Raw SQL, and the only raw SQL in the repository. Three things it must do in one
        // statement: create the row when the user has none, take the row's exclusive lock, and
        // advance the counter — all inside the caller's transaction, so InnoDB holds that lock
        // until COMMIT and a second writer cannot get its rank before the first is visible.
        // Splitting them — take a number, then write in another transaction — reopens the hole from
        // the other end: rank 11 committed before rank 10, and a client syncing in between takes
        // token 11 and never sees 10.
        //
        // When the row does not exist yet, there is nothing to lock: both sessions attempt the
        // INSERT, and the loser blocks on the winner's uncommitted primary-key index-record lock
        // until it commits, then runs as an UPDATE against the row that now exists. Same outcome —
        // one rank each, in commit order — reached by a different mechanism than the row-exists
        // path above.
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
        // LAST_INSERT_ID() would answer the auto-increment of a table that has none. The scalar
        // projection below is load-bearing, not incidental: it materialises no entity, so a
        // ContactSyncState already tracked in this context cannot poison the read with a stale,
        // pre-increment value. Returning the entity instead of the column would reopen that hole.
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

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new SyncState(created.Epoch, 0, 0);
        }
        catch (DbUpdateException)
        {
            // Two devices racing the first sync-collection on the same fresh book. The first row
            // written wins; the loser wants the row that now exists rather than an exception — that
            // is the whole point of "or create" — so it detaches its own failed insert and re-reads.
            context.Entry(created).State = EntityState.Detached;
            var winner = await ReadStateAsync(userId, cancellationToken);
            if (winner is null) throw;

            return winner;
        }
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
            // r.Cause == revision.Cause is not redundant with the Rejected guard above: it
            // constrains the STORED row's cause, not just the incoming one. Without it, a Put
            // revision that happens to carry the same hash would suppress a Rejected archive.
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
        // Oldest first, and capped: the sweep is unfiltered across the whole deployment, so an
        // uncapped pass is one query whose result set is however many rows a backlog has grown to.
        // Ordering by DeletedAt is what makes the cap safe for the watermark below — rank and
        // deletion time move together per user (PlaceTombstoneAsync takes a fresh rank and a fresh
        // stamp on every deletion, re-deletions included), so a cut by time takes a PREFIX of each
        // user's tombstones and the max it raises the watermark to is the max actually deleted.
        var doomed = await context.ContactTombstones
            .Where(t => t.DeletedAt < tombstonesBefore)
            .OrderBy(t => t.DeletedAt)
            .Take(MaxRowsPerSweep)
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

        // Loaded once for every affected user rather than one query per user inside the loop below:
        // the sweep is unfiltered across the whole deployment, so the loop can span many users and
        // an N+1 here is an N+1 on the busiest maintenance path there is.
        var userIds = highest.Keys.ToList();
        var affectedStates = await context.ContactSyncStates
            .Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, cancellationToken);

        foreach (var (userId, sequence) in highest)
        {
            // No state row means no epoch, which means no token for this user was ever issued —
            // there is nothing a watermark could protect, so skipping is not the silent-loss shape
            // this method exists to avoid; it is the absence of anything to lose.
            if (!affectedStates.TryGetValue(userId, out var state)) continue;

            // Never downwards, and this is also why two sweepers running at once cannot corrupt the
            // watermark — though not because a lost DELETE is harmless. RemoveRange on tracked
            // entities throws DbUpdateConcurrencyException when it finds zero rows affected, so two
            // concurrent sweeps reading overlapping doomed sets collide: the second to commit finds
            // its rows already gone, throws, and rolls its whole transaction back — watermark
            // included. The collision is loud, not silently absorbed; that is the real reason the
            // watermark cannot regress. It is also why running two sweepers at once is a fault to
            // avoid rather than a supported mode: nothing is lost, but the loser's transaction is
            // wasted work on a rollback loop.
            state.PrunedBelow = Math.Max(state.PrunedBelow, sequence);
        }

        context.ContactTombstones.RemoveRange(doomed);

        // Keys alone, then stubs to delete by. A revision carries the whole card it archived — up
        // to ContactStore.MaxCardBytes — and this sweep spans every user, so materialising the
        // entities to delete them puts thirty days of the deployment's overwritten cards on the
        // heap at once, for a DELETE that only ever needed the primary key.
        var staleRevisions = await context.ContactRevisions
            .Where(r => r.ReplacedAt < revisionsBefore)
            .OrderBy(r => r.ReplacedAt)
            .Select(r => r.Id)
            .Take(MaxRowsPerSweep)
            .ToListAsync(cancellationToken);
        // A stub only where nothing is tracked under that key: this context may already hold the
        // very revision an ArchiveAsync put there earlier in the same scope, and attaching a second
        // instance of it throws on the identity map. The sweeper opens its own scope, so the map is
        // empty there and this costs a lookup against nothing.
        var alreadyTracked = context.ContactRevisions.Local.ToDictionary(r => r.Id);
        context.ContactRevisions.RemoveRange(staleRevisions.Select(
            id => alreadyTracked.TryGetValue(id, out var held) ? held : new ContactRevision { Id = id }));

        // One SaveChanges, so the watermark and the removal commit together or not at all.
        await context.SaveChangesAsync(cancellationToken);

        // Either count AT the cap means more was waiting: the next daily pass takes it, and the
        // sweeper says so rather than reporting a number that reads as "everything old is gone".
        return new PruneOutcome(doomed.Count, staleRevisions.Count,
            doomed.Count == MaxRowsPerSweep || staleRevisions.Count == MaxRowsPerSweep);
    }
}
