using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// A user's groups — <see cref="IContactStore"/>'s other half, the one species the address book
/// refuses (décision 4). Every method is scoped by <c>userId</c>, so a group belonging to somebody
/// else is simply not found, and every id it takes for a member is looked up in the caller's own
/// book: an unknown, foreign or group id resolves to nothing and is skipped in silence.
/// </summary>
public interface IContactGroupStore
{
    /// <summary>Every group, each with the members this book actually holds.</summary>
    Task<IReadOnlyList<ContactGroupView>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Composes an empty group card, stores it and projects it. Fails on a name that is blank or
    /// over the column, and when the per-user cap is reached — groups count towards it (décision 18).
    /// </summary>
    Task<Result<ContactGroupView>> CreateAsync(Guid userId, string name, CancellationToken cancellationToken);

    /// <summary>Rewrites the card's FN and nothing else. Fails when not found.</summary>
    Task<Result> RenameAsync(Guid userId, Guid groupId, string name, CancellationToken cancellationToken);

    /// <summary>Removes the group and its member rows. The contacts it listed survive it.</summary>
    Task<Result> DeleteAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a batch of contacts to the group. Idempotent: a contact already listed is not written
    /// twice, and a batch that changes nothing takes neither a rank nor a revision.
    /// </summary>
    Task<Result> AddMembersAsync(
        Guid userId, Guid groupId, IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken);

    /// <summary>Removes a batch of contacts from the group, under the same rules.</summary>
    Task<Result> RemoveMembersAsync(
        Guid userId, Guid groupId, IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken);
}
