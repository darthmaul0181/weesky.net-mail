using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

public interface ISendingIdentityStore
{
    Task<IReadOnlyList<SendingIdentity>> GetAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>Replaces the account's whole set in one transaction — the PUT semantics.</summary>
    Task ReplaceAsync(string accountId, IReadOnlyList<SendingIdentity> identities, CancellationToken cancellationToken);
}
