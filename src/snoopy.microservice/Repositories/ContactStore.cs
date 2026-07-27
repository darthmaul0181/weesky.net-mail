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

    /// <summary>
    /// Folds every address, drops what folds together, and numbers what survives from 0. The
    /// position is reassigned here rather than taken from the caller: a gap or a repeat coming
    /// off the wire would leave two rows claiming to be the primary.
    /// </summary>
    private void AddAddresses(Guid contactId, IReadOnlyList<string> addresses)
    {
        var seen = new HashSet<string>();
        var position = 0;

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
