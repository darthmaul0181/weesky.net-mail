using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// A user's contacts. Addresses go in as the caller typed them and come back canonical; callers
/// never fold them themselves. Every method is scoped by <c>userId</c>, so a contact
/// belonging to somebody else is simply not found.
/// </summary>
public interface IContactStore
{
    Task<IReadOnlyList<ContactView>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates it and answers its new id. Fails only when the per-user cap is reached.</summary>
    Task<Result<Guid>> CreateAsync(Guid userId, ContactWrite contact, CancellationToken cancellationToken);

    /// <summary>Replaces names, favourite flag and the whole address list. Fails when not found.</summary>
    Task<Result> UpdateAsync(Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken);

    /// <summary>Removes it and its addresses. Fails when not found.</summary>
    Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Flips the favourite flag alone. Its own method because the star is toggled from a tile
    /// that holds a possibly stale copy of the contact — a whole-object write would clobber it.
    /// </summary>
    Task<Result> SetFavoriteAsync(Guid userId, Guid contactId, bool isFavorite, CancellationToken cancellationToken);
}
