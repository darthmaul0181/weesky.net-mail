using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The mailboxes a user attached to their session. Every method is scoped by <c>userId</c>, so
/// somebody else's account is simply not found.
/// </summary>
public interface IConnectedAccountStore
{
    Task<IReadOnlyList<ConnectedAccount>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<ConnectedAccount?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Also creates the default sending identity row in the same SaveChanges.</summary>
    Task<Result<ConnectedAccount>> CreateAsync(ConnectedAccount row, CancellationToken cancellationToken);

    Task UpdateCipherAsync(ConnectedAccount row, byte[] cipher, CancellationToken cancellationToken);

    /// <summary>Rewrites every cipher of the user in one SaveChanges — the ChangeSecret re-key.</summary>
    Task ReplaceCiphersAsync(
        Guid userId, IReadOnlyDictionary<Guid, byte[]> ciphers, CancellationToken cancellationToken);

    /// <summary>App-level cascade: removes the row plus its sending_identities and folder_role_overrides.</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}
