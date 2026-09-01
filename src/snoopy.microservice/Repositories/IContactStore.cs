using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// A user's contacts. Addresses go in as the caller typed them and come back canonical; callers
/// never fold them themselves. Every method is scoped by <c>userId</c>, so a contact
/// belonging to somebody else is simply not found — and, since 4e, by kind: this is the address
/// book, so a group card is not found either, whatever it is asked for (décision 4). The CardDAV
/// side reads its own way and serves both species; only the ceiling counts them both.
/// </summary>
public interface IContactStore
{
    Task<IReadOnlyList<ContactView>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The whole card of one contact. Null when this user does not own it.</summary>
    Task<ContactDetail?> GetAsync(Guid userId, Guid contactId, CancellationToken cancellationToken);

    /// <summary>
    /// The projected avatar and the card hash the caller turns into an ETag. Null when the
    /// contact carries no picture, and equally when this user does not own it.
    /// </summary>
    Task<(byte[] Bytes, string MediaType, string CardHash)?> GetPhotoAsync(
        Guid userId, Guid contactId, CancellationToken cancellationToken);

    /// <summary>Every contact of the user, in the shape the CSV export reads.</summary>
    Task<IReadOnlyList<ContactDetail>> ExportAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Composes the contact's card, stores it and projects it. Fails when the per-user cap is
    /// reached or the card overruns the 1 MB ceiling.
    /// </summary>
    Task<Result<Guid>> CreateAsync(Guid userId, ContactWrite contact, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites the contact's card from the write and re-projects it whole. Fails when not found,
    /// over the 1 MB ceiling, or when <see cref="ContactWrite.CardHash"/> is set and no longer
    /// matches the stored card (<see cref="ContactStore.CardMoved"/>) — opt-in, so a write that
    /// names no hash behaves exactly as before. A write that changes nothing leaves the card and
    /// its hash alone.
    /// </summary>
    Task<Result> UpdateAsync(Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken);

    /// <summary>Removes it and its addresses. Fails when not found.</summary>
    Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Flips the favourite flag alone. Its own method because the star is toggled from a tile
    /// that holds a possibly stale copy of the contact — a whole-object write would clobber it.
    /// </summary>
    Task<Result> SetFavoriteAsync(Guid userId, Guid contactId, bool isFavorite, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a batch and answers how many rows it actually held. An id this user does not own
    /// resolves to nothing and is skipped in silence: a batch may not half-fail, and telling an
    /// unknown id from a foreign one would say whether it exists — and a group card is skipped the
    /// same way, being no more a thing the address book addresses (décision 4).
    /// <paramref name="includeGroups"/> lifts that clause for the one caller speaking for the
    /// CardDAV collection, where both species are resources; spelled out at every call site rather
    /// than defaulted, so a product surface cannot acquire it by omission.
    /// </summary>
    Task<int> DeleteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, bool includeGroups, CancellationToken cancellationToken);

    /// <summary>Sets or clears the favourite flag over a batch, under the same silent-skip rule.</summary>
    Task<int> SetFavoriteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, bool isFavorite, CancellationToken cancellationToken);

    /// <summary>
    /// Merges a whole file into the book in one transaction. Never fails as a whole: a row that
    /// cannot be filed comes back in the outcome rather than as an error status.
    /// </summary>
    Task<ContactImportOutcome> ImportAsync(
        Guid userId, IReadOnlyList<ContactImportRow> rows, CancellationToken cancellationToken);

    /// <summary>
    /// One batch of the 4a backfill, over every user of the table: the contacts stored before the
    /// vCard model existed get the card, the hash and the projection that model requires of every
    /// row. Deliberately unscoped — it is an operator sweep, which is why its route carries the
    /// admin policy. The queue is <c>card_hash = ''</c>, so calling it again resumes where it
    /// stopped and calling it once more than needed answers <c>{ 0, 0 }</c>.
    /// </summary>
    Task<BackfillOutcome> BackfillAsync(int batchSize, CancellationToken cancellationToken);
}
