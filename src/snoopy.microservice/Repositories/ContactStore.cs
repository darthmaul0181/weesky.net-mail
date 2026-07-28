using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class ContactStore(PreferencesDbContext context) : IContactStore
{
    /// <summary>
    /// What bounds the table, and what bounds the payload: the whole book is fetched into the
    /// browser, so this is a transfer ceiling as much as a storage one. Far above real use — it
    /// guards against a runaway import, not against a user.
    /// </summary>
    internal const int MaxPerUser = 5000;

    // Interpolated, not spelled out, so the ceiling is written once.
    internal static readonly string CapReached =
        $"You have reached the maximum of {MaxPerUser} contacts";

    internal const string NotFound = "Contact not found";

    internal const string AmbiguousAddress =
        "An address on this row already belongs to more than one contact";

    internal const string AmbiguousName =
        "This row carries no address, and its name is on more than one contact";

    internal const string NoNameOrAddress = "Neither a name nor a valid e-mail address";

    // A separator no name can carry, so three parts fold into one key without ever colliding.
    private const char NamePartSeparator = '\0';

    internal static readonly string AddressCapReached =
        $"Only the first {ContactValidator.MaxAddressesPerContact} addresses were kept";

    public async Task<IReadOnlyList<ContactView>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Projected, not the whole entity: VCardRaw is MEDIUMTEXT and ContactView never carries
        // it, but materialising the entity would still pull it across the wire for up to
        // MaxPerUser rows on every page load.
        var contacts = await context.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Nickname, c.IsFavorite })
            .ToListAsync(cancellationToken);
        if (contacts.Count == 0) return [];

        // One query for every address rather than one per contact: the list is read whole on
        // every page load, so an N+1 here is N+1 round trips on the hot path. A correlated
        // subquery rather than an IN list: MariaDB cannot parametrise a collection, so an IN of
        // up to MaxPerUser GUIDs would be inlined as literal SQL, defeating the plan cache.
        var addresses = await context.ContactEmails.AsNoTracking()
            .Where(e => context.Contacts.Any(c => c.Id == e.ContactId && c.UserId == userId))
            .OrderBy(e => e.Position)
            .ToListAsync(cancellationToken);

        var byContact = addresses
            .GroupBy(e => e.ContactId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.Address).ToList());

        return [.. contacts.Select(c => new ContactView(
            c.Id, c.FirstName, c.LastName, c.Nickname, c.IsFavorite,
            byContact.TryGetValue(c.Id, out var found) ? found : []))];
    }

    public async Task<Result<Guid>> CreateAsync(
        Guid userId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
        if (stored >= MaxPerUser) return Result.Failure<Guid>(CapReached);

        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact
        {
            Id = id,
            UserId = userId,
            // A contact born here has no foreign UID, so its own id serves. The column stays
            // distinct from the key because an imported card brings a UID we must not overwrite.
            Uid = id.ToString(),
            FirstName = contact.FirstName,
            LastName = contact.LastName,
            Nickname = contact.Nickname,
            IsFavorite = contact.IsFavorite,
            Source = contact.Source,
            UpdatedAt = DateTime.UtcNow
        });
        AddAddresses(id, contact.Addresses);

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(id);
    }

    public async Task<Result> UpdateAsync(
        Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        row.FirstName = contact.FirstName;
        row.LastName = contact.LastName;
        row.Nickname = contact.Nickname;
        row.IsFavorite = contact.IsFavorite;
        row.UpdatedAt = DateTime.UtcNow;
        // Uid, VCardRaw and Source are deliberately untouched: the first is the identity a CardDAV
        // client syncs on, the second holds properties this UI cannot show and must not erase, the
        // third records an origin that editing does not change.

        // Replace rather than merge: the editor submits the list it displays, so an address the
        // user removed has to go. Removed then re-added, because a position is not a key and
        // reordering has to be able to move an address that stays.
        var existing = await context.ContactEmails
            .Where(e => e.ContactId == contactId)
            .ToListAsync(cancellationToken);
        context.ContactEmails.RemoveRange(existing);
        AddAddresses(contactId, contact.Addresses);

        // A single SaveChanges: the change tracker merges a Deleted+Added pair on the same key
        // into one Modified command, and splitting it would leave the contact with no addresses
        // at all between the two commits if the second one failed.
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        // The FK cascades in MariaDB, but the InMemory provider the tests run on enforces no FK
        // at all: deleting the children here is what makes the behaviour the same in both.
        var addresses = await context.ContactEmails
            .Where(e => e.ContactId == contactId)
            .ToListAsync(cancellationToken);
        context.ContactEmails.RemoveRange(addresses);
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

        var owners = new Dictionary<string, HashSet<Guid>>();
        var held = new Dictionary<Guid, HashSet<string>>();
        var nextPosition = new Dictionary<Guid, int>();
        foreach (var row in addressRows)
        {
            Register(owners, held, row.ContactId, row.Address);
            nextPosition[row.ContactId] = Math.Max(nextPosition.GetValueOrDefault(row.ContactId), row.Position + 1);
        }

        var named = new Dictionary<string, HashSet<Guid>>();
        foreach (var c in addressless) Index(named, NameKey(c.FirstName, c.LastName, c.Nickname), c.Id);

        var born = new Dictionary<Guid, Contact>();
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

            var targets = canonical
                .SelectMany(a => owners.TryGetValue(a, out var set) ? set : [])
                .Distinct().ToList();
            if (targets.Count > 1)
            {
                skipped++;
                errors.Add(new ContactImportError(row.Line, AmbiguousAddress));
                continue;
            }

            // The name is consulted only when the row brought no address at all: an address is the
            // stronger signal and has already decided.
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

            var kept = canonical.Take(ContactValidator.MaxAddressesPerContact).ToList();
            if (kept.Count < canonical.Count) errors.Add(new ContactImportError(row.Line, AddressCapReached));

            var id = Guid.NewGuid();
            var contact = new Contact
            {
                Id = id,
                UserId = userId,
                Uid = id.ToString(),
                FirstName = row.FirstName,
                LastName = row.LastName,
                Nickname = row.Nickname,
                IsFavorite = row.IsFavorite,
                Source = "imported",
                VCardRaw = row.VCard,
                UpdatedAt = DateTime.UtcNow
            };
            context.Contacts.Add(contact);
            born[id] = contact;
            AddAddresses(id, kept);
            nextPosition[id] = kept.Count;
            foreach (var address in kept) Register(owners, held, id, address);
            // Kept current as the file is read, or a name listed twice with no address would leave
            // two cards behind — the address index is kept current for the same reason.
            if (kept.Count == 0) Index(named, NameKey(row.FirstName, row.LastName, row.Nickname), id);
            created++;
        }

        await ApplyMergesAsync(userId, merges, born, nextPosition, cancellationToken);
        // One write for the whole file: a failure on the eight-hundredth row must not leave a book
        // half imported that no screen can describe.
        await context.SaveChangesAsync(cancellationToken);

        return new ContactImportOutcome(created, merged, skipped, failed, errors);
    }

    private async Task ApplyMergesAsync(
        Guid userId,
        List<(Guid Target, ContactImportRow Row, List<string> Addresses)> merges,
        Dictionary<Guid, Contact> born,
        Dictionary<Guid, int> nextPosition,
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

        foreach (var (target, row, addresses) in merges)
        {
            var contact = born.TryGetValue(target, out var fresh) ? fresh : byId.GetValueOrDefault(target);
            // A concurrent delete between the index query and this one leaves nothing to merge into;
            // the row is dropped rather than failing the whole file on a KeyNotFoundException.
            if (contact == null) continue;
            var changed = false;

            if (contact.FirstName == null && row.FirstName != null) { contact.FirstName = row.FirstName; changed = true; }
            if (contact.LastName == null && row.LastName != null) { contact.LastName = row.LastName; changed = true; }
            if (contact.Nickname == null && row.Nickname != null) { contact.Nickname = row.Nickname; changed = true; }
            if (!contact.IsFavorite && row.IsFavorite) { contact.IsFavorite = true; changed = true; }
            if (contact.VCardRaw == null && row.VCard != null) { contact.VCardRaw = row.VCard; changed = true; }

            if (addresses.Count > 0)
            {
                AddAddresses(target, addresses, nextPosition.GetValueOrDefault(target));
                nextPosition[target] = nextPosition.GetValueOrDefault(target) + addresses.Count;
                changed = true;
            }

            // Only when something moved: updated_at is what a future CardDAV ETag rests on, and a
            // replayed file that changes nothing must not make every client resync.
            if (changed) contact.UpdatedAt = DateTime.UtcNow;
        }
    }

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
    /// Folds every address, drops what folds together, and numbers what survives from 0. The
    /// position is reassigned here rather than taken from the caller: a gap or a repeat coming
    /// off the wire would leave two rows claiming to be the primary.
    /// </summary>
    private void AddAddresses(Guid contactId, IReadOnlyList<string> addresses, int startPosition = 0)
    {
        var seen = new HashSet<string>();
        var position = startPosition;

        foreach (var address in addresses)
        {
            var canonical = IdentityResolver.Canonical(address);
            if (!seen.Add(canonical)) continue;

            context.ContactEmails.Add(new ContactEmail
            {
                ContactId = contactId, Address = canonical, Position = position++
            });
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
