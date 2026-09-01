using System.Text;
using FolkerKinzel.VCards;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Services.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IDavContactWriter"/>
internal sealed class DavContactWriter(
    PreferencesDbContext context, ContactStore store, IContactSyncStore sync,
    ILogger<DavContactWriter> logger) : IDavContactWriter
{
    public async Task<DavWriteOutcome> PutAsync(
        Guid userId, string davName, string card, CancellationToken cancellationToken,
        bool createOnly = false, string? ifMatch = null)
    {
        if (Refusal(card, out var uid) is { } refused) return refused;

        try
        {
            return await GateAsync(userId, davName, card, uid, createOnly, ifMatch, cancellationToken);
        }
        // Before the DbUpdateException arm, which it would otherwise be swallowed by: EF wraps the
        // provider's 1205 inside one, and replaying a lock wait would only wait again.
        catch (Exception e) when (DavOutcomeTranslator.IsTransient(e))
        {
            logger.LogWarning(e,
                "PUT {DavName} for {UserId} lost a lock race; answering busy", davName, userId);
            context.ChangeTracker.Clear();
            return Refused(DavWriteStatus.Busy);
        }
        catch (DbUpdateException first)
        {
            // The race of two creating PUTs: the loser passes the existence pre-check and dies on a
            // unique index. Replayed once — which is what the same PUT arrived a second later would
            // have been: a replacement of the winner's row, or, when the winner landed the UID
            // under ANOTHER name, the conflict the replay's own holder check names.
            logger.LogWarning(first,
                "PUT {DavName} for {UserId} hit a unique index; translating instead of failing",
                davName, userId);
            context.ChangeTracker.Clear();

            try
            {
                return await GateAsync(userId, davName, card, uid, createOnly, ifMatch,
                    cancellationToken);
            }
            catch (DbUpdateException second)
            {
                logger.LogError(second,
                    "PUT {DavName} for {UserId} failed twice; answering busy", davName, userId);
                context.ChangeTracker.Clear();
                return Refused(DavWriteStatus.Busy);
            }
        }
    }

    public async Task<DavWriteOutcome> DeleteAsync(
        Guid userId, string davName, CancellationToken cancellationToken, string? ifMatch = null)
    {
        // The reader's visibility clause: a row the 4a backfill has not reached was never served,
        // and deleting what the protocol cannot see must be the same 404 an unknown name gets.
        var row = await context.Contacts.Visible(userId)
            .SingleOrDefaultAsync(c => c.DavName == davName, cancellationToken);
        if (row is null) return Refused(DavWriteStatus.NotFound);

        // The cheap net at this gate's own read; the decisive comparison is under the lock below.
        if (ifMatch is not null && !Holds(ifMatch, row))
            return Refused(DavWriteStatus.PreconditionFailed);

        try
        {
            return await store.InTransactionAsync(async () =>
            {
                var rank = await sync.NextSequenceAsync(userId, cancellationToken);

                // Re-read under the state lock: the archive keeps what is stored NOW, and the
                // decisive If-Match compares against it — a replacement committed since the read
                // above is the very version the header protects. A refusal rolls the rank back.
                if (!await ReloadAsync(row, cancellationToken)) return Refused(DavWriteStatus.NotFound);
                if (ifMatch is not null && !Holds(ifMatch, row))
                    return Refused(DavWriteStatus.PreconditionFailed);

                // ContactId left NULL: a delete revision outlives the row it describes.
                await sync.ArchiveAsync(new ContactRevision
                {
                    UserId = userId,
                    ContactId = null,
                    Uid = row.Uid,
                    DavName = davName,
                    CardHash = row.CardHash,
                    VCardRaw = row.VCardRaw!,
                    Cause = RevisionCause.Delete,
                    ReplacedAt = DateTime.UtcNow
                }, cancellationToken);

                await store.ClearProjectionAsync([row.Id], cancellationToken);
                context.Contacts.Remove(row);
                await context.SaveChangesAsync(cancellationToken);

                await sync.PlaceTombstoneAsync(userId, davName, rank, cancellationToken);
                return new DavWriteOutcome(DavWriteStatus.Deleted, null, null, rank);
            }, outcome => outcome.Status is DavWriteStatus.Deleted, cancellationToken);
        }
        catch (Exception e) when (DavOutcomeTranslator.IsTransient(e))
        {
            logger.LogWarning(e,
                "DELETE {DavName} for {UserId} lost a lock race; answering busy", davName, userId);
            context.ChangeTracker.Clear();
            return Refused(DavWriteStatus.Busy);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row vanished between the read and the write: this delete arrived second, and to
            // its sender that is the same 404 an absent name answers.
            context.ChangeTracker.Clear();
            return Refused(DavWriteStatus.NotFound);
        }
    }

    public async Task<DavWriteOutcome> DeleteAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            // The reader's visibility clause, as in DeleteAsync: what the protocol never served, it
            // cannot be asked to delete. Ids only — DeleteManyAsync re-reads each batch under its
            // lock. The read sits inside the try too: a transient failure here is exactly the lock
            // race the catch below answers Busy for, not a 500 escaping past it.
            var ids = await context.Contacts.Visible(userId)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0) return Emptied;

            var buried = await store.DeleteManyAsync(userId, ids, includeGroups: true, cancellationToken);
            logger.LogInformation("DELETE of the book for {UserId} buried {Count} cards", userId, buried);
            return Emptied;
        }
        catch (Exception e) when (DavOutcomeTranslator.IsTransient(e))
        {
            logger.LogWarning(e,
                "DELETE of the book for {UserId} lost a lock race; answering busy", userId);
            context.ChangeTracker.Clear();
            return Refused(DavWriteStatus.Busy);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The rows vanished under the batch — someone emptied the book first, and to this
            // sender that is the same 204 an already-empty book answers.
            context.ChangeTracker.Clear();
            return Emptied;
        }
    }

    public async Task<bool> ArchiveRejectedAsync(
        Guid userId, string davName, string card, CancellationToken cancellationToken)
    {
        // A revision may not outweigh what a stored card may: the ceiling is translated here
        // rather than surfacing as a database refusal on the 412 path.
        if (Encoding.UTF8.GetByteCount(card) > ContactStore.MaxCardBytes) return false;

        // It is an archive, not a card: a body that parses into nothing is kept with no UID.
        var uid = VCardImportMapper.UidOf(card);

        try
        {
            return await sync.ArchiveAsync(new ContactRevision
            {
                UserId = userId,
                ContactId = null,
                Uid = uid is { Length: <= VCardProjector.MaxUidLength } ? uid : null,
                DavName = davName,
                CardHash = ContactStore.CardHashOf(card),
                VCardRaw = card,
                Cause = RevisionCause.Rejected,
                ReplacedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception e) when (DavOutcomeTranslator.IsTransient(e))
        {
            // The archive is a courtesy beside a refusal already decided, and this insert is the
            // one write on the 412 path: a lock wait here would turn a correct 412 into the 500 a
            // client retries on the same card for ever — and its precondition would fail again.
            logger.LogWarning(e,
                "Archiving the refused body for {DavName} of {UserId} lost a lock race",
                davName, userId);
            context.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<DavWriteOutcome> GateAsync(
        Guid userId, string davName, string card, string? uid, bool createOnly, string? ifMatch,
        CancellationToken cancellationToken)
    {
        var row = await context.Contacts
            .SingleOrDefaultAsync(c => c.UserId == userId && c.DavName == davName, cancellationToken);

        // If-Match, first — RFC 7232 § 6 orders it before If-None-Match — at this gate's own read:
        // the cheap net over a row that vanished or moved since the edge's pre-check. A resource
        // the protocol cannot see is no current representation, so If-Match fails against it too.
        // The decisive comparison runs again below, under the state lock.
        if (ifMatch is not null && !Holds(ifMatch, row))
            return Refused(DavWriteStatus.PreconditionFailed);

        // Create-only, and the name already holds a VISIBLE resource: the creation race's loser,
        // whether the winner landed before this read or on the unique index the replay re-enters
        // through. Refused before anything else — no rank, no archive, above all no replacement of
        // what the winner just stored. An invisible pre-backfill row falls through: the protocol
        // never served it, so creating over it stays a creation.
        if (createOnly && row is { VCardRaw: not null } && row.CardHash.Length > 0)
            return Refused(DavWriteStatus.AlreadyExists);

        // RFC 6352 § 6.3.2.1: the UID must not be one ANOTHER resource already holds, and § 6.2.2's
        // DAV:href names that holder — never the request URI, which a client would re-read and
        // learn nothing from. A UID that merely changes under its own name is accepted: nothing
        // holds the new one, and the archive keeps the old identity. Refused before any lock.
        if (uid is not null && await HolderOfAsync(userId, uid, davName, cancellationToken) is { } incumbent)
            return Conflict(userId, incumbent.DavName);

        if (row is null)
        {
            var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
            if (stored >= ContactStore.MaxPerUser) return Refused(DavWriteStatus.BookFull);
        }

        // The invariant of 4a: every stored card carries a UID. Stamping one into a card that
        // declares none is the single transformation this path allows — and what mutes the ETag.
        var identity = uid ?? row?.Uid ?? Guid.NewGuid().ToString();
        var stamped = ContactStore.WithUid(card, identity);
        if (Encoding.UTF8.GetByteCount(stamped) > ContactStore.MaxCardBytes)
            return Refused(DavWriteStatus.TooLarge);

        // Byte-identical with what is already stored: nothing changes, so no transaction, no rank,
        // no client woken — the shape every idempotent DAVx5 retry takes.
        if (row is not null && string.Equals(row.VCardRaw, stamped, StringComparison.Ordinal))
            return new DavWriteOutcome(
                DavWriteStatus.Replaced, EtagOf(row.CardHash, stamped, card), null, row.SyncSequence);

        return await store.InTransactionAsync(async () =>
        {
            // The state row's lock FIRST, always, and before any contact row is touched — the same
            // order as the two existing gates, or they deadlock against each other.
            var rank = await sync.NextSequenceAsync(userId, cancellationToken);

            // Re-read under the lock: everything judged above was judged on a row a concurrent
            // writer may have replaced or removed since, and the archive below must keep what is
            // stored NOW — or that version never enters contact_revisions.
            if (row is not null && !await ReloadAsync(row, cancellationToken)) row = null;
            if (uid is null && row is not null && row.Uid != identity)
                stamped = ContactStore.WithUid(card, identity = row.Uid);
            var replacing = row is { VCardRaw: not null } && row.CardHash.Length > 0;

            // The decisive If-Match comparison — the only one a conditional write may trust: every
            // writer takes the state lock first, so this row is authoritative, and a tag that no
            // longer holds is the lost update the header exists to refuse. The rank rolls back
            // with the refusal (the commit predicate below), so no client is woken.
            if (ifMatch is not null && !Holds(ifMatch, row))
                return Refused(DavWriteStatus.PreconditionFailed);

            if (uid is not null)
            {
                // The unique index (user_id, uid) laid by 4a is the production guard; this read
                // under the state lock is what lets the refusal carry the conflicting href.
                var holder = await HolderOfAsync(userId, uid, davName, cancellationToken);
                if (holder is not null) return Conflict(userId, holder.DavName);
            }

            if (row is null)
            {
                row = new Contact
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Uid = identity,
                    IsFavorite = false,
                    Source = "carddav",
                    UpdatedAt = DateTime.UtcNow,
                    DavName = davName,
                    SyncSequence = rank
                };
                context.Contacts.Add(row);
            }
            else
            {
                // Archive before overwriting, in the same transaction — so under the same rank,
                // and never without it. No card, no revision (the backfill never reached this row).
                if (row.VCardRaw is not null)
                {
                    await sync.ArchiveAsync(new ContactRevision
                    {
                        UserId = userId,
                        ContactId = row.Id,
                        Uid = row.Uid,
                        DavName = row.DavName,
                        CardHash = row.CardHash,
                        VCardRaw = row.VCardRaw,
                        Cause = RevisionCause.Put,
                        ReplacedAt = DateTime.UtcNow
                    }, cancellationToken);
                }

                row.Uid = identity;
                row.UpdatedAt = DateTime.UtcNow;
                row.SyncSequence = rank;
            }

            // The write gate of 4a: id, user_id, is_favorite and source are untouched; everything
            // else is the projection of the card and is recomputed.
            await store.ApplyCardAsync(row, stamped, null, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // A tombstone and a living card must never coexist on one name: a sync-collection
            // would return both, and the order the client applies them in would decide the fate.
            await sync.LiftTombstoneAsync(userId, davName, cancellationToken);

            return new DavWriteOutcome(
                replacing ? DavWriteStatus.Replaced : DavWriteStatus.Created,
                EtagOf(row.CardHash, stamped, card), null, rank);
        }, outcome => outcome.Status is DavWriteStatus.Created or DavWriteStatus.Replaced,
            cancellationToken);
    }

    /// <summary>
    /// Every refusal the card alone decides, before any row is read. The multi-card refusal must
    /// PRECEDE the projection: <c>VCardProjector</c> would silently keep the first card only.
    /// </summary>
    private static DavWriteOutcome? Refusal(string card, out string? uid)
    {
        uid = null;

        if (Encoding.UTF8.GetByteCount(card) > ContactStore.MaxCardBytes)
            return Refused(DavWriteStatus.TooLarge);

        // RFC 2426's ABNF excludes CTL from every value. The bytes are stored and served as they
        // arrive, so a bell accepted once is one a client re-reads and refuses on every sync after.
        if (HasControlCharacter(card)) return Refused(DavWriteStatus.InvalidCard);

        // An address object resource is ONE vCard (RFC 6352 § 5.1).
        var chunks = VCardSplitter.Split(card);
        if (chunks.Count != 1 || !VCardSplitter.IsComplete(chunks[0]))
            return Refused(DavWriteStatus.InvalidCard);

        // The bytes are stored as they arrive, so bytes around the card would be served back as
        // part of it: nothing but whitespace may surround the one card.
        var start = card.IndexOf(chunks[0].Text, StringComparison.Ordinal);
        if (!card.AsSpan(0, start).IsWhiteSpace()
            || !card.AsSpan(start + chunks[0].Text.Length).IsWhiteSpace())
            return Refused(DavWriteStatus.InvalidCard);

        // VERSION is mandatory in 3.0 and 4.0 alike: a card without one is no card of either. With
        // one outside what supported-address-data announces — old Android still exports 2.1 — the
        // card can be perfectly readable while being refusable, under its own condition.
        var version = VCardImportMapper.RawValueOf(card, "VERSION");
        if (version is null) return Refused(DavWriteStatus.InvalidCard);
        if (!AddressDataFilter.Versions.Contains(version, StringComparer.Ordinal))
            return Refused(DavWriteStatus.UnsupportedVersion);

        var lines = VCardComposer.LogicalLines(VCardComposer.CanonicalLineBreaks(card))
            .Select(VCardComposer.Unfold)
            .ToList();

        // Every line of a card is a contentline (RFC 6350 § 3.3), so one without a name/value colon
        // is a value spilled onto a line of its own — the reader silently keeps it as a property.
        if (lines.Exists(line => VCardComposer.IndexOutsideQuotes(line, ':') < 0))
            return Refused(DavWriteStatus.InvalidCard);

        // One resource, one identity (§ 5.1). Answered no-uid-conflict, the client is sent to read
        // an href that names nothing; the group prefix counts, or "item1.UID" hides the second one.
        if (lines.Count(line =>
                VCardComposer.NameOf(line).Equals("UID", StringComparison.OrdinalIgnoreCase)) > 1)
        {
            return Refused(DavWriteStatus.InvalidCard);
        }

        try
        {
            if (Vcf.Parse(card).FirstOrDefault() is null) return Refused(DavWriteStatus.InvalidCard);
        }
        catch
        {
            return Refused(DavWriteStatus.InvalidCard);
        }

        // A UID the column cannot hold: truncating would rotate the identity every client syncs
        // on, so the card is refused whole — décision 14, the same the import applies.
        uid = VCardImportMapper.UidOf(card);
        if (uid is { Length: > VCardProjector.MaxUidLength })
        {
            uid = null;
            return Refused(DavWriteStatus.InvalidCard);
        }

        return null;
    }

    /// <summary>CR, LF and HTAB excepted: they are what the line structure and folding are made
    /// of, and refusing them would refuse the correct clients this guard exists to protect.</summary>
    private static bool HasControlCharacter(string card)
    {
        foreach (var character in card)
        {
            if (character is '\r' or '\n' or '\t') continue;
            if (character < ' ' || character is '\u007F') return true;
        }

        return false;
    }

    /// <summary>True when the row is a current representation the If-Match header covers — the
    /// STRONG comparison, shared with the edge through <see cref="EntityTagMatcher"/>.</summary>
    private static bool Holds(string ifMatch, Contact? row) =>
        row is { VCardRaw: not null } && row.CardHash.Length > 0
        && EntityTagMatcher.Match(ifMatch, DavProperties.EntityTag(row.CardHash));

    /// <summary>Reloads a row read before the state lock; false when it no longer exists.</summary>
    private async Task<bool> ReloadAsync(Contact row, CancellationToken cancellationToken)
    {
        var entry = context.Entry(row);
        await entry.ReloadAsync(cancellationToken);
        return entry.State is not EntityState.Detached;
    }

    // The null arm is spelled out: in SQL a NULL dav_name fails `!=` and the nameless holder —
    // a row the backfill has not named — would silently escape the conflict it causes.
    private Task<Contact?> HolderOfAsync(
        Guid userId, string uid, string davName, CancellationToken cancellationToken) =>
        context.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.UserId == userId && c.Uid == uid && (c.DavName == null || c.DavName != davName),
                cancellationToken);

    /// <summary>
    /// The quoted tag when the stored bytes are the received ones, null when they differ: the RFC
    /// then requires NO ETag, so the client re-reads instead of believing it holds what it sent.
    /// </summary>
    private static string? EtagOf(string cardHash, string stored, string received) =>
        string.Equals(stored, received, StringComparison.Ordinal)
            ? DavProperties.EntityTag(cardHash)
            : null;

    private static DavWriteOutcome Refused(DavWriteStatus status) => new(status, null, null, 0);

    private static readonly DavWriteOutcome Emptied = new(DavWriteStatus.Deleted, null, null, 0);

    private static DavWriteOutcome Conflict(Guid userId, string? holderName) =>
        new(DavWriteStatus.UidConflict, null,
            holderName is null ? null : DavPaths.Card(userId, holderName), 0);
}
