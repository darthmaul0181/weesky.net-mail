using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class ContactStore(PreferencesDbContext context) : IContactStore
{
    /// <summary>
    /// What bounds the table, and what bounds the payload: the whole book is fetched into the
    /// browser, so this is a transfer ceiling as much as a storage one. Far above real use — it
    /// guards against a runaway import, not against a user.
    /// </summary>
    internal const int MaxPerUser = 5000;

    /// <summary>
    /// What one stored card may weigh. Measured at every <c>vcard_raw</c> write, not only on
    /// import: an interminable NOTE posted by an editor reaches the same ceiling as a file does.
    /// </summary>
    internal const int MaxCardBytes = 1024 * 1024;

    // Interpolated, not spelled out, so the ceiling is written once.
    internal static readonly string CapReached =
        $"You have reached the maximum of {MaxPerUser} contacts";

    internal const string NotFound = "Contact not found";

    internal static readonly string CardTooLarge =
        $"The contact's vCard exceeds {MaxCardBytes / 1024 / 1024} MB";

    internal const string AmbiguousAddress =
        "An address on this row already belongs to more than one contact";

    internal const string AmbiguousName =
        "This row carries no address, and its name is on more than one contact";

    internal const string NoNameOrAddress = "Neither a name nor a valid e-mail address";

    // A separator no name can carry, so three parts fold into one key without ever colliding.
    private const char NamePartSeparator = '\0';

    /// <summary>What ends a vCard property name, group prefix included.</summary>
    private static readonly char[] NameEnd = [';', ':'];

    internal static readonly string AddressCapReached =
        $"Only the first {ContactValidator.MaxAddressesPerContact} addresses were kept";

    public async Task<IReadOnlyList<ContactView>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Projected, not the whole entity: VCardRaw is MEDIUMTEXT and ContactView never carries
        // it, but materialising the entity would still pull it across the wire for up to
        // MaxPerUser rows on every page load.
        var contacts = await context.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Nickname, c.IsFavorite, c.DisplayName })
            .ToListAsync(cancellationToken);
        if (contacts.Count == 0) return [];

        // One query for every address rather than one per contact: the list is read whole on
        // every page load, so an N+1 here is N+1 round trips on the hot path. A correlated
        // subquery rather than an IN list: MariaDB cannot parametrise a collection, so an IN of
        // up to MaxPerUser GUIDs would be inlined as literal SQL, defeating the plan cache.
        // Ordered on (pref, position): the card's rank is the composer's handle, not a rank the
        // user chose — PREF is what says which address is the primary (décision 5 bis).
        var addresses = await context.ContactEmails.AsNoTracking()
            .Where(e => context.Contacts.Any(c => c.Id == e.ContactId && c.UserId == userId))
            .OrderBy(e => e.Pref).ThenBy(e => e.Position)
            .ToListAsync(cancellationToken);

        // Ids alone: a photo is 50 to 300 KB and the whole book descends in one answer, so the
        // list carries a boolean and the picture leaves by its own route (décision 12).
        var withPhoto = await context.ContactPhotos.AsNoTracking()
            .Where(p => context.Contacts.Any(c => c.Id == p.ContactId && c.UserId == userId))
            .Select(p => p.ContactId)
            .ToListAsync(cancellationToken);
        var pictured = withPhoto.ToHashSet();

        // Deduplicated for display only: the same address may legitimately sit on two properties
        // with two types, and the table keeps both.
        var byContact = addresses
            .GroupBy(e => e.ContactId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.Address).Distinct().ToList());

        return [.. contacts.Select(c => new ContactView(
            c.Id, c.FirstName, c.LastName, c.Nickname, c.IsFavorite,
            byContact.TryGetValue(c.Id, out var found) ? found : [],
            c.DisplayName, pictured.Contains(c.Id)))];
    }

    public async Task<ContactDetail?> GetAsync(Guid userId, Guid contactId, CancellationToken cancellationToken)
    {
        var row = await Scalars(context.Contacts.AsNoTracking()
            .Where(c => c.Id == contactId && c.UserId == userId)).FirstOrDefaultAsync(cancellationToken);
        if (row == null) return null;

        // Scoped by the row above rather than by a correlated subquery: one contact is known to
        // be this user's before any child row is read.
        return Detail(row,
            await context.ContactEmails.AsNoTracking()
                .Where(e => e.ContactId == contactId).ToListAsync(cancellationToken),
            await context.ContactPhones.AsNoTracking()
                .Where(p => p.ContactId == contactId).ToListAsync(cancellationToken),
            await context.ContactAddresses.AsNoTracking()
                .Where(a => a.ContactId == contactId).ToListAsync(cancellationToken),
            await context.ContactPhotos.AsNoTracking()
                .AnyAsync(p => p.ContactId == contactId, cancellationToken));
    }

    public async Task<(byte[] Bytes, string MediaType, string CardHash)?> GetPhotoAsync(
        Guid userId, Guid contactId, CancellationToken cancellationToken)
    {
        // Joined rather than read twice: the hash lives on the contact and is what the caller
        // turns into the ETag it serves the picture with.
        var found = await context.Contacts.AsNoTracking()
            .Where(c => c.Id == contactId && c.UserId == userId)
            .Join(context.ContactPhotos.AsNoTracking(), c => c.Id, p => p.ContactId,
                (c, p) => new { p.Bytes, p.MediaType, c.CardHash })
            .FirstOrDefaultAsync(cancellationToken);

        return found == null ? null : (found.Bytes, found.MediaType, found.CardHash);
    }

    public async Task<IReadOnlyList<ContactDetail>> ExportAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var rows = await Scalars(context.Contacts.AsNoTracking().Where(c => c.UserId == userId))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return [];

        // The list's anti-N+1 shape, one family at a time and joined in memory.
        var emails = (await context.ContactEmails.AsNoTracking()
            .Where(e => context.Contacts.Any(c => c.Id == e.ContactId && c.UserId == userId))
            .ToListAsync(cancellationToken)).ToLookup(e => e.ContactId);
        var phones = (await context.ContactPhones.AsNoTracking()
            .Where(p => context.Contacts.Any(c => c.Id == p.ContactId && c.UserId == userId))
            .ToListAsync(cancellationToken)).ToLookup(p => p.ContactId);
        var postal = (await context.ContactAddresses.AsNoTracking()
            .Where(a => context.Contacts.Any(c => c.Id == a.ContactId && c.UserId == userId))
            .ToListAsync(cancellationToken)).ToLookup(a => a.ContactId);
        var pictured = (await context.ContactPhotos.AsNoTracking()
            .Where(p => context.Contacts.Any(c => c.Id == p.ContactId && c.UserId == userId))
            .Select(p => p.ContactId)
            .ToListAsync(cancellationToken)).ToHashSet();

        return [.. rows.Select(r => Detail(r, emails[r.Id], phones[r.Id], postal[r.Id], pictured.Contains(r.Id)))];
    }

    public async Task<Result<Guid>> CreateAsync(
        Guid userId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
        if (stored >= MaxPerUser) return Result.Failure<Guid>(CapReached);

        var id = Guid.NewGuid();
        var row = new Contact
        {
            Id = id,
            UserId = userId,
            // A contact born here has no foreign UID, so its own id serves. The column stays
            // distinct from the key because an imported card brings a UID we must not overwrite.
            Uid = id.ToString(),
            IsFavorite = contact.IsFavorite,
            Source = contact.Source,
            UpdatedAt = DateTime.UtcNow
        };
        context.Contacts.Add(row);

        // The names and every other modelled column are posed by the projection, not copied from
        // the write: the card is what they describe (décision 1).
        var written = await WriteCardAsync(row, VCardComposer.ComposeNew(row.Uid, contact), cancellationToken);
        if (written.IsFailure)
        {
            // Nothing is saved here, but a tracked row would ride along on the next SaveChanges
            // any other store of this scoped context makes.
            context.Entry(row).State = EntityState.Detached;
            return Result.Failure<Guid>(written.Error);
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(id);
    }

    public async Task<Result> UpdateAsync(
        Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        // Uid and Source are deliberately untouched: the first is the identity a CardDAV client
        // syncs on, the second records an origin that editing does not change. VCardRaw was of
        // that company until this slice: the card is now what the contact is, and this is where
        // it is rewritten — values replaced in place, so what we do not model survives.
        var card = row.VCardRaw == null
            ? VCardComposer.ComposeNew(row.Uid, contact)
            : VCardComposer.Compose(row.VCardRaw, row.Uid, contact);
        var written = await WriteCardAsync(row, card, cancellationToken);
        if (written.IsFailure) return written;

        row.IsFavorite = contact.IsFavorite;
        row.UpdatedAt = DateTime.UtcNow;

        // A single SaveChanges: the change tracker merges a Deleted+Added pair on the same key
        // into one Modified command, and splitting it would leave the contact with no child rows
        // at all between the two commits if the second one failed.
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        await ClearProjectionAsync([contactId], cancellationToken);
        context.Contacts.Remove(row);

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetFavoriteAsync(
        Guid userId, Guid contactId, bool isFavorite, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        row.IsFavorite = isFavorite;
        row.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<int> DeleteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await context.Contacts
            .Where(c => c.UserId == userId && ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return 0;

        await ClearProjectionAsync([.. rows.Select(r => r.Id)], cancellationToken);
        context.Contacts.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<int> SetFavoriteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, bool isFavorite, CancellationToken cancellationToken)
    {
        var rows = await context.Contacts
            .Where(c => c.UserId == userId && ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsFavorite = isFavorite;
            row.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<ContactImportOutcome> ImportAsync(
        Guid userId, IReadOnlyList<ContactImportRow> rows, CancellationToken cancellationToken)
    {
        var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
        // The same correlated subquery ListAsync uses: MariaDB cannot parametrise a collection, so
        // an IN list of up to MaxPerUser ids would be inlined and defeat the plan cache.
        var addressRows = await context.ContactEmails.AsNoTracking()
            .Where(e => context.Contacts.Any(c => c.Id == e.ContactId && c.UserId == userId))
            .ToListAsync(cancellationToken);

        // Only the address-less contacts: one that has addresses is reachable through the address
        // index, and the exporter always writes the addresses a contact has — so a row carrying a
        // name and nothing else can only ever be describing a contact that has none.
        var addressless = await context.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId && !context.ContactEmails.Any(e => e.ContactId == c.Id))
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Nickname })
            .ToListAsync(cancellationToken);

        // The third index, and the one consulted first: a card's UID is an identity its owner
        // chose, where an address is a coincidence and a name a guess (décision 14).
        var uidOwners = new Dictionary<string, Guid>();
        foreach (var c in await context.Contacts.AsNoTracking()
                     .Where(c => c.UserId == userId)
                     .Select(c => new { c.Id, c.Uid })
                     .ToListAsync(cancellationToken))
            uidOwners[c.Uid] = c.Id;

        var owners = new Dictionary<string, HashSet<Guid>>();
        var held = new Dictionary<Guid, HashSet<string>>();
        foreach (var row in addressRows) Register(owners, held, row.ContactId, row.Address);

        var named = new Dictionary<string, HashSet<Guid>>();
        foreach (var c in addressless) Index(named, NameKey(c.FirstName, c.LastName, c.Nickname), c.Id);

        var born = new Dictionary<Guid, Contact>();
        var pending = new Dictionary<Guid, PendingCard>();
        var merges = new List<(Guid Target, ContactImportRow Row, List<string> Addresses)>();
        var errors = new List<ContactImportError>();
        int created = 0, merged = 0, skipped = 0, failed = 0;

        foreach (var row in rows)
        {
            var canonical = row.Addresses.Select(IdentityResolver.Canonical).Distinct().ToList();
            if (row.FirstName == null && row.LastName == null && row.Nickname == null && canonical.Count == 0)
            {
                failed++;
                errors.Add(new ContactImportError(row.Line, NoNameOrAddress));
                continue;
            }

            List<Guid> targets;
            if (row.Uid != null && uidOwners.TryGetValue(row.Uid, out var byUid))
            {
                targets = [byUid];
            }
            else
            {
                targets = canonical
                    .SelectMany(a => owners.TryGetValue(a, out var set) ? set : [])
                    .Distinct().ToList();
                if (targets.Count > 1)
                {
                    skipped++;
                    errors.Add(new ContactImportError(row.Line, AmbiguousAddress));
                    continue;
                }

                // The name is consulted only when the row brought no address at all: an address is
                // the stronger signal and has already decided.
                if (canonical.Count == 0
                    && named.TryGetValue(NameKey(row.FirstName, row.LastName, row.Nickname), out var sharing))
                {
                    if (sharing.Count > 1)
                    {
                        skipped++;
                        errors.Add(new ContactImportError(row.Line, AmbiguousName));
                        continue;
                    }

                    targets = [.. sharing];
                }
            }

            if (targets.Count == 1)
            {
                var target = targets[0];
                HashSet<string> mine = held.TryGetValue(target, out var found) ? found : [];
                var room = ContactValidator.MaxAddressesPerContact - mine.Count;
                var incoming = canonical.Where(a => !mine.Contains(a)).ToList();
                if (incoming.Count > room)
                {
                    incoming = [.. incoming.Take(Math.Max(room, 0))];
                    errors.Add(new ContactImportError(row.Line, AddressCapReached));
                }

                foreach (var address in incoming) Register(owners, held, target, address);
                merges.Add((target, row, incoming));
                merged++;
                continue;
            }

            if (stored + created >= MaxPerUser)
            {
                skipped++;
                errors.Add(new ContactImportError(row.Line, CapReached));
                continue;
            }

            // A card the file brought is projected whole (décision 8): the cap bounds what we
            // compose ourselves, never what a foreign card carries.
            var kept = row.VCard != null
                ? canonical : [.. canonical.Take(ContactValidator.MaxAddressesPerContact)];
            if (kept.Count < canonical.Count) errors.Add(new ContactImportError(row.Line, AddressCapReached));

            var id = Guid.NewGuid();
            var contact = new Contact
            {
                Id = id,
                UserId = userId,
                // A card brings the UID it is synchronised on; without one, the generated id serves.
                Uid = row.Uid ?? id.ToString(),
                FirstName = row.FirstName,
                LastName = row.LastName,
                Nickname = row.Nickname,
                IsFavorite = row.IsFavorite,
                Source = "imported",
                UpdatedAt = DateTime.UtcNow
            };
            context.Contacts.Add(contact);
            born[id] = contact;
            // Composed here, written once at the end: a later row of the same file may still merge
            // into this contact, and two writes would project it twice.
            pending[id] = new PendingCard(contact, row.Line,
                row.VCard ?? VCardComposer.ComposeNew(contact.Uid, Composed(row, contact, kept)));
            uidOwners[contact.Uid] = id;
            foreach (var address in kept) Register(owners, held, id, address);
            // Kept current as the file is read, or a name listed twice with no address would leave
            // two cards behind — the address and UID indexes are kept current for the same reason.
            if (kept.Count == 0) Index(named, NameKey(row.FirstName, row.LastName, row.Nickname), id);
            created++;
        }

        // Four queries for the whole file, whatever the number of merges: what the targets already
        // hold is what re-projecting them has to clear.
        var cache = await LoadProjectionAsync(
            [.. merges.Select(m => m.Target).Where(id => !born.ContainsKey(id)).Distinct()],
            cancellationToken);
        await ApplyMergesAsync(userId, merges, born, pending, cache, uidOwners, cancellationToken);

        foreach (var (id, item) in pending)
        {
            var written = await WriteCardAsync(item.Contact, item.Card, cancellationToken, cache);
            if (written.IsSuccess) continue;

            errors.Add(new ContactImportError(item.Line, written.Error));
            if (!born.ContainsKey(id)) continue;
            // Nothing of a contact whose card cannot be stored: décision 1 admits no card-less row.
            context.Entry(item.Contact).State = EntityState.Detached;
            created--;
            failed++;
        }

        // One write for the whole file: a failure on the eight-hundredth row must not leave a book
        // half imported that no screen can describe.
        await context.SaveChangesAsync(cancellationToken);

        return new ContactImportOutcome(created, merged, skipped, failed, errors);
    }

    public async Task<BackfillOutcome> BackfillAsync(int batchSize, CancellationToken cancellationToken)
    {
        // No user filter: an operator sweep over the whole table (décision 15). card_hash = '' is
        // the queue, which is what makes the pass resumable and a replay free.
        var batch = await context.Contacts
            .Where(c => c.CardHash == "")
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (batch.Count == 0) return new BackfillOutcome(0, 0);

        // Four queries for the whole batch, the import path's shape: what these contacts already
        // hold is both what the card is reconciled against and what re-projecting them clears.
        var cache = await LoadProjectionAsync([.. batch.Select(c => c.Id)], cancellationToken);

        var processed = 0;
        foreach (var row in batch)
        {
            IReadOnlyList<string> addresses = [.. cache.AddressesOf(row.Id)];
            // Reconciled, never recomposed. 3a left vcard_raw untouched on every edit, so a card
            // written before 4a may carry a stale N, FN and EMAIL while its columns are current —
            // projecting it first would restore the old name over the edit (spec, § Le rattrapage).
            var card = row.VCardRaw == null
                ? VCardComposer.ComposeNew(row.Uid, WriteOf(row, addresses, cache))
                : VCardComposer.Reconcile(row.VCardRaw, row.Uid,
                    new ReconcileWrite(row.FirstName, row.LastName, row.Nickname, addresses));

            // A card over the ceiling leaves the row in the queue rather than half-converted; the
            // batch then answers a Processed below its size, which is what tells the operator.
            if ((await WriteCardAsync(row, card, cancellationToken, cache)).IsFailure) continue;

            row.UpdatedAt = DateTime.UtcNow;
            processed++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return new BackfillOutcome(processed,
            await context.Contacts.CountAsync(c => c.CardHash == "", cancellationToken));
    }

    private async Task ApplyMergesAsync(
        Guid userId,
        List<(Guid Target, ContactImportRow Row, List<string> Addresses)> merges,
        Dictionary<Guid, Contact> born,
        Dictionary<Guid, PendingCard> pending,
        ProjectionCache cache,
        Dictionary<string, Guid> uidOwners,
        CancellationToken cancellationToken)
    {
        if (merges.Count == 0) return;

        var wanted = merges.Select(m => m.Target).Where(id => !born.ContainsKey(id)).Distinct().ToList();
        var tracked = wanted.Count == 0
            ? []
            : await context.Contacts
                .Where(c => c.UserId == userId && wanted.Contains(c.Id))
                .ToListAsync(cancellationToken);
        var byId = tracked.ToDictionary(c => c.Id);
        var holdings = new Dictionary<Guid, Holdings>();

        foreach (var (target, row, addresses) in merges)
        {
            var contact = born.TryGetValue(target, out var fresh) ? fresh : byId.GetValueOrDefault(target);
            // A concurrent delete between the index query and this one leaves nothing to merge into;
            // the row is dropped rather than failing the whole file on a KeyNotFoundException.
            if (contact == null) continue;

            if (!holdings.TryGetValue(target, out var held))
                // A contact born of this same file has no column written yet — its verbatim card is
                // the only account of what it holds. Every other target's columns already are it.
                holdings[target] = held = born.ContainsKey(target)
                    ? Holdings.Of(VCardProjector.Project(pending[target].Card))
                    : Holdings.Of(contact, cache);

            // Nothing is overwritten, and no column is written here: the card is what fills them,
            // and a column posed beside it is a column that drifts from it (décision 1).
            var fill = held.Fill(row, addresses);
            var movesTheCard = !fill.IsEmpty;
            var changed = movesTheCard;
            // The star has no vCard property of its own, so it alone never moves the card.
            if (!contact.IsFavorite && row.IsFavorite) { contact.IsFavorite = true; changed = true; }

            if (movesTheCard)
                pending[target] = Filled(
                    contact, pending.GetValueOrDefault(target), row, fill, cache, uidOwners);

            // Only when something moved: updated_at is what a future CardDAV ETag rests on, and a
            // replayed file that changes nothing must not make every client resync.
            if (changed) contact.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The target's card with the merge folded in, composed and never yet written. A target with
    /// no card of its own takes the incoming one — the third door of décision 1, the only one that
    /// keeps a foreign card's X- properties — but only when that card repeats everything the
    /// target holds: the projection is total, so what it does not repeat, it erases. Otherwise the
    /// target's own state becomes the card the merge fills, and nothing is lost.
    /// </summary>
    private static PendingCard Filled(
        Contact contact, PendingCard? pending, ContactImportRow row, MergeWrite fill,
        ProjectionCache cache, Dictionary<string, Guid> uidOwners)
    {
        var current = pending?.Card ?? contact.VCardRaw;
        if (current == null && row.VCard != null && LosesNothing(contact, cache, row.VCard))
            // The column and the card must agree on the identity a client synchronises on before
            // the write: failing adoption, the composer stamps the column's UID on the card.
            return new PendingCard(contact, row.Line,
                Adopt(contact, row.Uid, uidOwners)
                    ? row.VCard
                    : VCardComposer.MergeFill(row.VCard, contact.Uid, NoFill));

        current ??= VCardComposer.ComposeNew(
            contact.Uid, WriteOf(contact, cache.AddressesOf(contact.Id), cache));
        return new PendingCard(contact, pending?.Line ?? row.Line,
            VCardComposer.MergeFill(current, contact.Uid, fill));
    }

    /// <summary>
    /// What a created contact's card is composed from: the columns the file carried outside the
    /// model when it had any, the contact's own otherwise — with the capped address list either
    /// way, so the card and the index it is registered under never hold different addresses.
    /// </summary>
    private static ContactWrite Composed(
        ContactImportRow row, Contact contact, IReadOnlyList<string> kept) =>
        row.Write is { } write
            ? write with { Addresses = [.. kept.Select(a => new ContactWriteEmail(null, a, string.Empty))] }
            : WriteOf(contact, kept, null);

    /// <summary>Nothing to fill: the card is re-emitted only so the composer stamps its UID.</summary>
    private static readonly MergeWrite NoFill = new(null, null, null, []);

    /// <summary>
    /// Whether the incoming card repeats every column and every child row the target already
    /// holds. A card posed verbatim is projected whole and destructively, so anything it leaves
    /// out is erased — a merge matched on one shared address would silently empty the rest.
    /// </summary>
    private static bool LosesNothing(Contact contact, ProjectionCache cache, string card)
    {
        var incoming = VCardProjector.Project(card);
        return Scalars(contact).Zip(Scalars(incoming)).All(p => p.First == null || p.First == p.Second)
            && cache.AddressesOf(contact.Id).All(a => incoming.Addresses.Any(e => e.Address == a))
            && cache.Phones[contact.Id].All(p => incoming.Phones.Any(i => i.Number == p.Number))
            && cache.PostalAddresses[contact.Id].All(a => incoming.PostalAddresses.Any(i => Same(i, a)))
            && (!cache.Photos[contact.Id].Any() || incoming.Photo != null);
    }

    private static IEnumerable<string?> Scalars(Contact c) =>
        [c.FirstName, c.LastName, c.Nickname, c.DisplayName, c.MiddleName, c.NamePrefix,
         c.NameSuffix, c.Organization, c.Department, c.JobTitle, c.Birthday, c.Website, c.Notes];

    private static IEnumerable<string?> Scalars(ContactProjection p) =>
        [p.FirstName, p.LastName, p.Nickname, p.DisplayName, p.MiddleName, p.NamePrefix,
         p.NameSuffix, p.Organization, p.Department, p.JobTitle, p.Birthday, p.Website, p.Notes];

    private static bool Same(ProjectedAddress incoming, ContactAddress held) =>
        incoming.PoBox == held.PoBox && incoming.Extended == held.Extended
        && incoming.Street == held.Street && incoming.Locality == held.Locality
        && incoming.Region == held.Region && incoming.PostalCode == held.PostalCode
        && incoming.Country == held.Country;

    /// <summary>
    /// Puts the card's own UID on the column, so a card stored verbatim and its contact name one
    /// identity. Refused when the card carries none, when it overruns the column, or when another
    /// contact of this user already answers to it — a duplicate <c>uq_contacts_user_uid</c> would
    /// fail the whole file.
    /// </summary>
    private static bool Adopt(Contact contact, string? uid, Dictionary<string, Guid> uidOwners)
    {
        if (uid is not { Length: > 0 } || uid.Length > VCardProjector.MaxUidLength) return false;
        if (uid == contact.Uid) return true;
        if (uidOwners.ContainsKey(uid)) return false;

        uidOwners.Remove(contact.Uid);
        uidOwners[uid] = contact.Id;
        contact.Uid = uid;
        return true;
    }

    /// <summary>A contact's card between its composition and the single write that stores it.</summary>
    private sealed record PendingCard(Contact Contact, int Line, string Card);

    /// <summary>
    /// What a merge target already holds, kept current as the rows of one file fill it. The columns
    /// are written once, at the very end, so a second row aimed at the same target would otherwise
    /// read it as empty — overwriting what the first row posed, and doubling its families.
    /// </summary>
    private sealed class Holdings
    {
        private string? firstName, lastName, nickname, middleName, namePrefix, nameSuffix,
            displayName, organization, department, jobTitle, birthday, website, notes;
        private bool phones, postalAddresses;

        internal static Holdings Of(Contact c, ProjectionCache cache) => new()
        {
            firstName = c.FirstName, lastName = c.LastName, nickname = c.Nickname,
            middleName = c.MiddleName, namePrefix = c.NamePrefix, nameSuffix = c.NameSuffix,
            displayName = c.DisplayName, organization = c.Organization, department = c.Department,
            jobTitle = c.JobTitle, birthday = c.Birthday, website = c.Website, notes = c.Notes,
            phones = cache.Phones[c.Id].Any(), postalAddresses = cache.PostalAddresses[c.Id].Any(),
        };

        internal static Holdings Of(ContactProjection card) => new()
        {
            firstName = card.FirstName, lastName = card.LastName, nickname = card.Nickname,
            middleName = card.MiddleName, namePrefix = card.NamePrefix, nameSuffix = card.NameSuffix,
            displayName = card.DisplayName, organization = card.Organization,
            department = card.Department, jobTitle = card.JobTitle, birthday = card.Birthday,
            website = card.Website, notes = card.Notes,
            phones = card.Phones.Count > 0, postalAddresses = card.PostalAddresses.Count > 0,
        };

        /// <summary>
        /// What this row adds and nothing more: every field the target does not hold yet, each
        /// family only when it holds none of it. Marks what it hands out, so the next row aimed
        /// here sees the target as it will be, not as the columns still describe it.
        /// </summary>
        internal MergeWrite Fill(ContactImportRow row, IReadOnlyList<string> addresses)
        {
            var offered = row.Write;
            return new MergeWrite(
                Take(ref firstName, row.FirstName),
                Take(ref lastName, row.LastName),
                Take(ref nickname, row.Nickname),
                addresses,
                Take(ref middleName, offered?.MiddleName),
                Take(ref namePrefix, offered?.NamePrefix),
                Take(ref nameSuffix, offered?.NameSuffix),
                Take(ref displayName, offered?.DisplayName),
                Take(ref organization, offered?.Organization),
                Take(ref department, offered?.Department),
                Take(ref jobTitle, offered?.JobTitle),
                Take(ref birthday, offered?.Birthday),
                Take(ref website, offered?.Website),
                Take(ref notes, offered?.Notes),
                Take(ref phones, offered?.Phones),
                Take(ref postalAddresses, offered?.PostalAddresses));
        }

        private static string? Take(ref string? held, string? offered) =>
            held != null || offered == null ? null : held = offered;

        // All or nothing: two spellings of one number are indistinguishable without a
        // normalisation neither TEL nor ADR has, so a target holding any is handed none.
        private static IReadOnlyList<T>? Take<T>(ref bool held, IReadOnlyList<T>? offered)
        {
            if (held || offered is not { Count: > 0 }) return null;
            held = true;
            return offered;
        }
    }

    /// <summary>
    /// A contact's stored state as a write, so the composer can give a card to one that has none
    /// without losing what its child tables hold. The photo has no write door (décision 12), and a
    /// contact with no card has no photo row either: only a projection ever writes one.
    /// </summary>
    private static ContactWrite WriteOf(
        Contact contact, IEnumerable<string> addresses, ProjectionCache? held) =>
        new(contact.FirstName, contact.LastName, contact.Nickname, contact.DisplayName,
            contact.MiddleName, contact.NamePrefix, contact.NameSuffix, contact.Organization,
            contact.Department, contact.JobTitle, contact.Birthday, contact.Website, contact.Notes,
            contact.IsFavorite,
            [.. addresses.Select(a => new ContactWriteEmail(null, a, string.Empty))],
            [.. (held?.Phones[contact.Id] ?? []).OrderBy(p => p.Position)
                .Select(p => new ContactWritePhone(null, p.Number, p.Type))],
            [.. (held?.PostalAddresses[contact.Id] ?? []).OrderBy(a => a.Position)
                .Select(a => new ContactWriteAddress(null, a.Type, a.PoBox, a.Extended, a.Street,
                    a.Locality, a.Region, a.PostalCode, a.Country))],
            contact.Source);

    private static void Register(
        Dictionary<string, HashSet<Guid>> owners, Dictionary<Guid, HashSet<string>> held,
        Guid contactId, string address)
    {
        Index(owners, address, contactId);

        if (!held.TryGetValue(contactId, out var addresses)) held[contactId] = addresses = [];
        addresses.Add(address);
    }

    private static void Index(Dictionary<string, HashSet<Guid>> index, string key, Guid contactId)
    {
        if (!index.TryGetValue(key, out var contacts)) index[key] = contacts = [];
        contacts.Add(contactId);
    }

    /// <summary>
    /// The three name parts as one key, trimmed and lower-cased invariantly and joined on a
    /// character no name can carry. Deliberately not <see cref="IdentityResolver.Canonical"/>:
    /// that one folds addresses, and borrowing it here would blur what either of them means.
    /// </summary>
    private static string NameKey(string? first, string? last, string? nickname) =>
        string.Join(NamePartSeparator,
            (first ?? string.Empty).Trim().ToLowerInvariant(),
            (last ?? string.Empty).Trim().ToLowerInvariant(),
            (nickname ?? string.Empty).Trim().ToLowerInvariant());

    /// <summary>
    /// The contact columns a card is read from. A read shape rather than the entity, for the
    /// reason <see cref="ListAsync"/> gives: <c>vcard_raw</c> is MEDIUMTEXT and materialising the
    /// entity would drag up to <see cref="MaxPerUser"/> × <see cref="MaxCardBytes"/> of card into
    /// one list on a route any authenticated user can call.
    /// </summary>
    private sealed record ContactScalars(
        Guid Id, string? FirstName, string? LastName, string? Nickname, string? DisplayName,
        string? MiddleName, string? NamePrefix, string? NameSuffix, string? Organization,
        string? Department, string? JobTitle, string? Birthday, string? Website, string? Notes,
        bool IsFavorite);

    private static IQueryable<ContactScalars> Scalars(IQueryable<Contact> contacts) =>
        contacts.Select(c => new ContactScalars(
            c.Id, c.FirstName, c.LastName, c.Nickname, c.DisplayName, c.MiddleName, c.NamePrefix,
            c.NameSuffix, c.Organization, c.Department, c.JobTitle, c.Birthday, c.Website, c.Notes,
            c.IsFavorite));

    /// <summary>
    /// The one read shape the card and the export share. Every family is ordered on
    /// <c>(pref, position)</c> — display order, not the card's rank (décision 5 bis).
    /// </summary>
    private static ContactDetail Detail(
        ContactScalars row, IEnumerable<ContactEmail> emails, IEnumerable<ContactPhone> phones,
        IEnumerable<ContactAddress> postal, bool hasPhoto) =>
        new(row.Id, row.FirstName, row.LastName, row.Nickname, row.DisplayName, row.MiddleName,
            row.NamePrefix, row.NameSuffix, row.Organization, row.Department, row.JobTitle,
            row.Birthday, row.Website, row.Notes, row.IsFavorite, hasPhoto,
            [.. emails.OrderBy(e => e.Pref).ThenBy(e => e.Position).Select(e =>
                new ContactDetailEmail(e.Position, e.Address, e.Type, e.Pref, e.Params, e.GroupName))],
            [.. phones.OrderBy(p => p.Pref).ThenBy(p => p.Position).Select(p =>
                new ContactDetailPhone(p.Position, p.Number, p.Type, p.Pref, p.Params, p.GroupName))],
            [.. postal.OrderBy(a => a.Pref).ThenBy(a => a.Position).Select(a =>
                new ContactDetailAddress(a.Position, a.Type, a.Pref, a.Params, a.GroupName,
                    a.PoBox, a.Extended, a.Street, a.Locality, a.Region, a.PostalCode, a.Country))]);

    /// <summary>
    /// The one place <c>vcard_raw</c> is written: composing is the caller's, stamping the UID,
    /// hashing and projecting are here. A hash computed by callers is a hash a caller will forget.
    /// </summary>
    private async Task<Result> WriteCardAsync(
        Contact row, string card, CancellationToken cancellationToken, ProjectionCache? loaded = null)
    {
        // First of all, because the insertion adds bytes: the ceiling, the comparison and the hash
        // all describe the card as it will be stored, or card_hash stops describing it.
        card = WithUid(card, row.Uid);
        if (Encoding.UTF8.GetByteCount(card) > MaxCardBytes) return Result.Failure(CardTooLarge);
        // The composer refreshes REV on every output, so a card that changed nothing is never
        // byte-equal to the stored one. Compared without that line it is, and décision 9 asks
        // that a write changing nothing change neither the card nor the ETag resting on it.
        if (row.CardHash.Length > 0 && row.VCardRaw != null && SameIgnoringRev(row.VCardRaw, card))
            return Result.Success();

        row.VCardRaw = card;
        row.CardHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(card)));
        await ReplaceProjectionAsync(row, VCardProjector.Project(card), loaded, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// The card with a <c>UID</c> equal to the column, inserted where RFC 6350 puts it — right
    /// after <c>VERSION</c>, failing that after <c>BEGIN</c> — when, and only when, the card
    /// declares none. RFC 6352 wants a UID per address resource, and synthesising one at serving
    /// time would divorce the bytes served from the bytes hashed; replacing one a card already
    /// carries would rotate the identity every CardDAV client syncs on. Textual, never a
    /// re-serialisation, which is what would cost the byte equality a strong ETag rests on.
    /// Logical lines, group prefixes and the first <c>END:VCARD</c> as the bound are read as
    /// <see cref="VCardComposer.NameOf"/> reads them; text that is no card is left intact.
    /// </summary>
    internal static string WithUid(string card, string uid)
    {
        var version = -1;
        var versionBreak = string.Empty;
        var begin = -1;
        var beginBreak = string.Empty;
        var cardBreak = string.Empty;
        var logical = new StringBuilder();
        var index = 0;

        while (index < card.Length)
        {
            var name = string.Empty;
            var continuation = false;
            string lineBreak;
            logical.Clear();
            do
            {
                var start = index + (continuation ? 1 : 0); // a fold's leading blank is not content
                while (index < card.Length && card[index] is not ('\r' or '\n')) index++;
                // Only the head is ever unfolded, and only while the name is unsettled, which the
                // first ';' or ':' settles: a folded PHOTO must not be rebuilt to be named.
                if (name.Length == 0)
                {
                    var head = card.IndexOfAny(NameEnd, start, index - start);
                    logical.Append(card, start, (head < 0 ? index : head + 1) - start);
                    name = VCardComposer.NameOf(logical.ToString());
                }

                lineBreak = LineBreakAt(card, ref index);
                continuation = true;
            }
            while (lineBreak.Length > 0 && index < card.Length && card[index] is ' ' or '\t');

            if (cardBreak.Length == 0) cardBreak = lineBreak;
            if (name.Equals("UID", StringComparison.OrdinalIgnoreCase)) return card;
            if (name.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
            if (version < 0 && name.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
                (version, versionBreak) = (index, lineBreak);
            else if (begin < 0 && name.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
                (begin, beginBreak) = (index, lineBreak);
        }

        var at = version >= 0 ? version : begin;
        if (at < 0) return card;

        // A break in the column is impossible by construction — one logical line, capped at 255 —
        // and stripped rather than assumed away: injected, it would forge a line or end the card.
        var line = "UID:" + uid.Replace("\r", string.Empty).Replace("\n", string.Empty);
        var lineEnding = version >= 0 ? versionBreak : beginBreak;
        return lineEnding.Length > 0
            ? string.Concat(card[..at], line, lineEnding, card[at..])
            : card + (cardBreak.Length > 0 ? cardBreak : "\r\n") + line;
    }

    /// <summary>The break at <paramref name="index"/>, consumed; empty at the end of the text.</summary>
    private static string LineBreakAt(string card, ref int index)
    {
        if (index >= card.Length) return string.Empty;

        var text = card[index] != '\r' ? "\n"
            : index + 1 < card.Length && card[index + 1] == '\n' ? "\r\n" : "\r";
        index += text.Length;
        return text;
    }

    private static bool SameIgnoringRev(string stored, string candidate) =>
        WithoutRev(stored).SequenceEqual(WithoutRev(candidate));

    // Line endings are normalised along the way: a card imported with bare LF and recomposed with
    // CRLF says nothing different, and rewriting it would burn an ETag for a newline.
    private static List<string> WithoutRev(string card) =>
    [
        .. card.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith("REV:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("REV;", StringComparison.OrdinalIgnoreCase))
    ];

    /// <summary>
    /// Décision 3: the projection is total and destructive. Every child row goes and is rewritten
    /// from the card, because a projection that updates what changed diverges from it in silence.
    /// </summary>
    private async Task ReplaceProjectionAsync(
        Contact row, ContactProjection projection, ProjectionCache? loaded,
        CancellationToken cancellationToken)
    {
        // A contact that is not in the database yet has no child row to clear; an import's are
        // already in hand, and clearing them from there is what keeps a whole file at four queries.
        if (loaded != null) loaded.Clear(context, row.Id);
        else if (context.Entry(row).State is EntityState.Unchanged or EntityState.Modified)
            await ClearProjectionAsync([row.Id], cancellationToken);

        row.FirstName = projection.FirstName;
        row.LastName = projection.LastName;
        row.Nickname = projection.Nickname;
        row.DisplayName = projection.DisplayName;
        row.MiddleName = projection.MiddleName;
        row.NamePrefix = projection.NamePrefix;
        row.NameSuffix = projection.NameSuffix;
        row.Organization = projection.Organization;
        row.Department = projection.Department;
        row.JobTitle = projection.JobTitle;
        row.Birthday = projection.Birthday;
        row.Website = projection.Website;
        row.Notes = projection.Notes;

        foreach (var email in projection.Addresses)
            context.ContactEmails.Add(new ContactEmail
            {
                ContactId = row.Id,
                // Folded on the way in: the column collates binary, so a casing difference would
                // split one address into two rows nothing can reconcile.
                Address = IdentityResolver.Canonical(email.Address),
                Position = email.Line.Position, Type = email.Line.Type, Pref = email.Line.Pref,
                Params = email.Line.Params, GroupName = email.Line.GroupName
            });

        foreach (var phone in projection.Phones)
            context.ContactPhones.Add(new ContactPhone
            {
                ContactId = row.Id, Number = phone.Number,
                Position = phone.Line.Position, Type = phone.Line.Type, Pref = phone.Line.Pref,
                Params = phone.Line.Params, GroupName = phone.Line.GroupName
            });

        foreach (var postal in projection.PostalAddresses)
            context.ContactAddresses.Add(new ContactAddress
            {
                ContactId = row.Id,
                PoBox = postal.PoBox, Extended = postal.Extended, Street = postal.Street,
                Locality = postal.Locality, Region = postal.Region, PostalCode = postal.PostalCode,
                Country = postal.Country,
                Position = postal.Line.Position, Type = postal.Line.Type, Pref = postal.Line.Pref,
                Params = postal.Line.Params, GroupName = postal.Line.GroupName
            });

        if (projection.Photo is { } photo)
            context.ContactPhotos.Add(new ContactPhoto
            {
                ContactId = row.Id, MediaType = photo.MediaType, Bytes = photo.Bytes
            });
    }

    /// <summary>
    /// The FK cascades in MariaDB, but the InMemory provider the tests run on enforces no FK at
    /// all: loading and removing the four families here is what makes the two behave alike.
    /// </summary>
    private async Task ClearProjectionAsync(
        IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken)
    {
        var loaded = await LoadProjectionAsync(contactIds, cancellationToken);
        foreach (var contactId in contactIds) loaded.Clear(context, contactId);
    }

    /// <summary>
    /// Every child row of the contacts an import merges into, loaded tracked in four queries.
    /// Re-projecting them then costs no query per contact, whatever the size of the file.
    /// </summary>
    private async Task<ProjectionCache> LoadProjectionAsync(
        IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken)
    {
        if (contactIds.Count == 0) return ProjectionCache.Of([], [], [], []);

        return ProjectionCache.Of(
            await context.ContactEmails.Where(e => contactIds.Contains(e.ContactId)).ToListAsync(cancellationToken),
            await context.ContactPhones.Where(p => contactIds.Contains(p.ContactId)).ToListAsync(cancellationToken),
            await context.ContactAddresses.Where(a => contactIds.Contains(a.ContactId)).ToListAsync(cancellationToken),
            await context.ContactPhotos.Where(p => contactIds.Contains(p.ContactId)).ToListAsync(cancellationToken));
    }

    private sealed record ProjectionCache(
        ILookup<Guid, ContactEmail> Emails, ILookup<Guid, ContactPhone> Phones,
        ILookup<Guid, ContactAddress> PostalAddresses, ILookup<Guid, ContactPhoto> Photos)
    {
        internal static ProjectionCache Of(
            List<ContactEmail> emails, List<ContactPhone> phones,
            List<ContactAddress> postal, List<ContactPhoto> photos) =>
            new(emails.ToLookup(e => e.ContactId), phones.ToLookup(p => p.ContactId),
                postal.ToLookup(a => a.ContactId), photos.ToLookup(p => p.ContactId));

        /// <summary>What a card-less contact already holds, in the order it will re-enter a card.</summary>
        internal IEnumerable<string> AddressesOf(Guid contactId) =>
            Emails[contactId].OrderBy(e => e.Position).Select(e => e.Address);

        internal void Clear(PreferencesDbContext context, Guid contactId)
        {
            context.ContactEmails.RemoveRange(Emails[contactId]);
            context.ContactPhones.RemoveRange(Phones[contactId]);
            context.ContactAddresses.RemoveRange(PostalAddresses[contactId]);
            context.ContactPhotos.RemoveRange(Photos[contactId]);
        }
    }

    /// <summary>
    /// Scoped by user on purpose: a contact belonging to somebody else must be indistinguishable
    /// from one that does not exist, so the controller can answer 404 without leaking it.
    /// </summary>
    private async Task<Contact?> FindAsync(Guid userId, Guid contactId, CancellationToken cancellationToken) =>
        await context.Contacts.FirstOrDefaultAsync(
            c => c.Id == contactId && c.UserId == userId, cancellationToken);
}
