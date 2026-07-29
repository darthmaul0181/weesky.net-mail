using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The curated From list, scoped by user and by account — the empty <c>accountId</c> meaning
/// the primary mailbox.
/// </summary>
public interface ISendingIdentityStore
{
    Task<IReadOnlyList<SendingIdentity>> GetAsync(
        Guid userId, string accountId, CancellationToken cancellationToken);

    /// <summary>Replaces the account's whole set in one transaction — the PUT semantics.</summary>
    Task ReplaceAsync(Guid userId, string accountId, IReadOnlyList<SendingIdentity> identities,
        CancellationToken cancellationToken);
}
