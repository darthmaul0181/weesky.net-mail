using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Services.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The group half of the book. Takes the concrete <see cref="ContactStore"/>, as
/// <see cref="DavContactWriter"/> does: the write gate — the transaction wrapper, the card
/// application and the projection — is shared rather than duplicated, so a group card travels the
/// exact same path a contact's does (décision 20).
/// </summary>
internal sealed class ContactGroupStore(
    PreferencesDbContext context, ContactStore store, IContactSyncStore sync) : IContactGroupStore
{
    public async Task<IReadOnlyList<ContactGroupView>> ListAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var groups = await context.Contacts.AsNoTracking().GroupCards()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.DisplayName })
            .ToListAsync(cancellationToken);
        if (groups.Count == 0) return [];

        // The frontier between two books lives entirely in this join (décision 2): a MEMBER is a
        // bare UID, so what makes it this user's member is that a contact of THIS user carries it.
        // The UID is tried under both its forms — a card imported as UID:urn:uuid:… stores the
        // prefix, the MEMBER value never does. Written as a filtered cross join rather than a
        // join on a constant, which EF translates badly; the member side is narrowed to this
        // user's groups by the correlated subquery ContactStore uses throughout, never an IN list
        // MariaDB cannot parametrise.
        // The prefixed branch matches on the TAIL, not on a concatenated constant: contacts.uid
        // collates binary, and décision 7 wants the nine-character prefix recognised whatever its
        // case while the UID itself stays case-sensitive. No LOWER() in the SQL — it would fold the
        // whole head under a rule the collation and the CLR spell differently; the head is confirmed
        // below by StripUrnUuid, the one place that rule is written.
        var resolved = await (
            from m in context.ContactGroupMembers.AsNoTracking().Where(m => context.Contacts.Any(
                g => g.Id == m.GroupId && g.UserId == userId && g.Kind == ContactKinds.Group))
            from c in context.Contacts.AsNoTracking().Individuals().Where(c => c.UserId == userId)
            where c.Uid == m.MemberUid
                || (c.Uid.Length == VCardProjector.UrnUuidPrefix.Length + m.MemberUid.Length
                    && c.Uid.Substring(VCardProjector.UrnUuidPrefix.Length) == m.MemberUid)
            select new { m.GroupId, MemberId = c.Id, m.Position, c.Uid, m.MemberUid })
            .ToListAsync(cancellationToken);

        var byGroup = resolved
            .Where(r => r.Uid == r.MemberUid || VCardProjector.StripUrnUuid(r.Uid) == r.MemberUid)
            .ToLookup(r => r.GroupId);
        return
        [
            .. groups.Select(g => new ContactGroupView(g.Id, g.DisplayName ?? string.Empty,
                [.. byGroup[g.Id].OrderBy(r => r.Position).Select(r => r.MemberId)]))
        ];
    }

    public async Task<Result<ContactGroupView>> CreateAsync(
        Guid userId, string name, CancellationToken cancellationToken)
    {
        // The controller already validated; re-run here so no door reaches the column unchecked.
        var validated = ContactValidator.ValidateGroupName(name);
        if (validated.IsFailure) return Result.Failure<ContactGroupView>(validated.Error);

        // Both species counted: the cap bounds what the book weighs, not what one screen shows.
        var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
        if (stored >= ContactStore.MaxPerUser)
            return Result.Failure<ContactGroupView>(ContactStore.CapReached);

        var id = Guid.NewGuid();
        var davName = DavName.ForContact(id);

        return await store.InTransactionAsync(async () =>
        {
            // The state row's lock FIRST, before any contact row is touched — the order every
            // other write gate takes, or they deadlock against each other.
            var rank = await sync.NextSequenceAsync(userId, cancellationToken);

            var row = new Contact
            {
                Id = id,
                UserId = userId,
                Uid = id.ToString(),
                Source = "manual",
                UpdatedAt = DateTime.UtcNow,
                DavName = davName,
                SyncSequence = rank
            };
            context.Contacts.Add(row);

            // Kind and display_name are posed by the projection, never copied from the request.
            await store.ApplyCardAsync(
                row, VCardComposer.ComposeNewGroup(row.Uid, validated.Value), null, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await sync.LiftTombstoneAsync(userId, davName, cancellationToken);

            // Homonymy is allowed: no name is unique, so nothing is answered but what was made.
            return Result.Success(new ContactGroupView(id, validated.Value, []));
        }, outcome => outcome.IsSuccess, cancellationToken);
    }

    public async Task<Result> RenameAsync(
        Guid userId, Guid groupId, string name, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.ValidateGroupName(name);
        if (validated.IsFailure) return Result.Failure(validated.Error);

        var row = await FindAsync(userId, groupId, cancellationToken);
        if (row?.VCardRaw is null) return Result.Failure(ContactStore.NotFound);

        return await EditCardAsync(
            userId, row, r => VCardComposer.RenameGroup(r.VCardRaw!, validated.Value), cancellationToken);
    }

    public Task<Result> AddMembersAsync(
        Guid userId, Guid groupId, IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken) =>
        EditMembersAsync(userId, groupId, contactIds, adding: true, cancellationToken);

    public Task<Result> RemoveMembersAsync(
        Guid userId, Guid groupId, IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken) =>
        EditMembersAsync(userId, groupId, contactIds, adding: false, cancellationToken);

    public async Task<Result> DeleteAsync(
        Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, groupId, cancellationToken);
        if (row is null) return Result.Failure(ContactStore.NotFound);

        return await store.InTransactionAsync<Result>(async () =>
        {
            var rank = await sync.NextSequenceAsync(userId, cancellationToken);

            // Under the lock, so the archive keeps the version stored now, not the one read above.
            // Assumed gap, as in EditCardAsync: Kind is not re-checked, so a card a DAV PUT turned
            // into a contact between the find and the lock is still deleted as the group it was.
            if (!await ReloadAsync(row, cancellationToken)) return Result.Failure(ContactStore.NotFound);
            var davName = row.DavName;

            if (row.VCardRaw is not null)
            {
                // ContactId left NULL: a delete revision outlives the row it describes.
                await sync.ArchiveAsync(new ContactRevision
                {
                    UserId = userId,
                    ContactId = null,
                    Uid = row.Uid,
                    DavName = davName,
                    CardHash = row.CardHash,
                    VCardRaw = row.VCardRaw,
                    Cause = RevisionCause.Delete,
                    ReplacedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            // The member rows go with it — the FK cascades in MariaDB, the InMemory provider
            // enforces none, and this is the one call that makes the two behave alike. The
            // contacts those rows pointed at are untouched (décision 7).
            await store.ClearProjectionAsync([groupId], cancellationToken);
            context.Contacts.Remove(row);
            // Décision 7 on the third door: a group nested in another (décision 9) leaves it here,
            // in the same transaction, or the parent keeps a MEMBER line naming nothing.
            await store.StripFromGroupsAsync(
                userId, ContactStore.Forms(row.Uid), [groupId], rank, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            if (davName is not null)
                await sync.PlaceTombstoneAsync(userId, davName, rank, cancellationToken);

            return Result.Success();
        }, outcome => outcome.IsSuccess, cancellationToken);
    }

    /// <summary>
    /// Both member batches, which differ only in the direction. The whole decision — which ids
    /// resolve, and whether any of them would change the card — is taken BEFORE the transaction:
    /// a batch that changes nothing must take neither a rank nor a revision, and a rank consumed
    /// for nothing wakes every client of this book.
    /// </summary>
    private async Task<Result> EditMembersAsync(
        Guid userId, Guid groupId, IReadOnlyList<Guid> contactIds, bool adding,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, groupId, cancellationToken);
        if (row?.VCardRaw is null) return Result.Failure(ContactStore.NotFound);

        var uids = await ResolveAsync(userId, contactIds, cancellationToken);
        var delta = Delta(uids, row.VCardRaw, adding);
        if (delta.Count == 0) return Result.Success();

        return await EditCardAsync(userId, row, r =>
        {
            // Re-filtered against the card as re-read under the lock: AddGroupMember is not
            // idempotent, so a member another writer added since would land twice.
            var under = Delta(delta, r.VCardRaw!, adding);
            return under.Count == 0
                ? null
                : under.Aggregate(r.VCardRaw!,
                    adding ? VCardComposer.AddGroupMember : VCardComposer.RemoveGroupMember);
        }, cancellationToken);
    }

    /// <summary>What of <paramref name="uids"/> the card does not yet carry, or still carries.</summary>
    private static List<string> Delta(IReadOnlyList<string> uids, string card, bool adding)
    {
        if (uids.Count == 0) return [];

        var held = VCardProjector.Project(card).Members
            .Select(m => m.MemberUid).ToHashSet(StringComparer.Ordinal);
        return [.. uids.Where(u => held.Contains(u) != adding)];
    }

    /// <summary>
    /// The ids of the batch turned into the UIDs a MEMBER line names — never a field of the
    /// request: a UID read from the store is a value this book stored, whereas a raw one could
    /// carry a line break and split the line it is written on. An unknown id, another book's, and
    /// a group's all resolve to nothing here, which is what makes them a silent no-op.
    /// </summary>
    private async Task<List<string>> ResolveAsync(
        Guid userId, IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken)
    {
        if (contactIds.Count == 0) return [];

        var uids = await context.Contacts.AsNoTracking().Individuals()
            .Where(c => c.UserId == userId && contactIds.Contains(c.Id))
            .Select(c => c.Uid)
            .ToListAsync(cancellationToken);

        // Stripped before it is written: AddGroupMember prefixes the value itself, so a stored
        // UID already carrying urn:uuid: would otherwise be emitted with the prefix twice.
        return [.. uids.Select(VCardProjector.StripUrnUuid).Where(u => u.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The whole path of one group-card write (décision 20): rank first, re-read under the lock,
    /// archive, apply — hash and projection included — and the new rank on the row. The body
    /// answers whether it wrote, and only a write commits: an edit that composes nothing new
    /// rolls the rank back rather than waking every client of this book for no change.
    /// Assumed gap, symmetric with ContactStore.UpdateAsync: the reload does not re-check Kind, so
    /// a card a DAV PUT turned into a contact between the find and the lock is still edited here.
    /// </summary>
    private async Task<Result> EditCardAsync(
        Guid userId, Contact row, Func<Contact, string?> edit, CancellationToken cancellationToken)
    {
        var written = await store.InTransactionAsync<Result<bool>>(async () =>
        {
            var rank = await sync.NextSequenceAsync(userId, cancellationToken);

            // Re-read under the state lock: it, not a card hash, is what makes this
            // read-modify-write a critical section — the race then touches only the row in play.
            if (!await ReloadAsync(row, cancellationToken) || row.VCardRaw is null)
                return Result.Failure<bool>(ContactStore.NotFound);

            var card = edit(row);
            if (card is null || card == row.VCardRaw) return Result.Success(false);

            await sync.ArchiveAsync(new ContactRevision
            {
                UserId = userId,
                ContactId = row.Id,
                Uid = row.Uid,
                DavName = row.DavName,
                CardHash = row.CardHash,
                VCardRaw = row.VCardRaw,
                Cause = RevisionCause.Webmail,
                ReplacedAt = DateTime.UtcNow
            }, cancellationToken);

            await store.ApplyCardAsync(row, card, null, cancellationToken);
            row.UpdatedAt = DateTime.UtcNow;
            row.SyncSequence = rank;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(true);
        }, outcome => outcome.IsSuccess && outcome.Value, cancellationToken);

        return written.IsSuccess ? Result.Success() : Result.Failure(written.Error);
    }

    /// <summary>Reloads a row read before the state lock; false when it no longer exists.</summary>
    private async Task<bool> ReloadAsync(Contact row, CancellationToken cancellationToken)
    {
        var entry = context.Entry(row);
        await entry.ReloadAsync(cancellationToken);
        return entry.State is not EntityState.Detached;
    }

    /// <summary>
    /// Scoped by user and by kind: a group belonging to somebody else, and a contact whatever its
    /// owner, must both be indistinguishable from an id that does not exist — the controller then
    /// answers 404 without leaking either.
    /// </summary>
    private Task<Contact?> FindAsync(Guid userId, Guid groupId, CancellationToken cancellationToken) =>
        context.Contacts.GroupCards()
            .FirstOrDefaultAsync(c => c.Id == groupId && c.UserId == userId, cancellationToken);
}
