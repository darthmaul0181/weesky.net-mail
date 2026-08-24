using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

internal interface IContactSyncStore
{
    /// Advances the counter under the state row's own exclusive lock and answers the new rank.
    /// Creates the row at seq = 0 with a fresh epoch when it is missing. MUST be called inside a
    /// transaction the caller owns, and FIRST — before any contact row is touched.
    Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken);

    /// The state as it stands, creating nothing. Null when the user has never had one — a getctag
    /// on an empty book answers 0 without writing.
    Task<SyncState?> ReadStateAsync(Guid userId, CancellationToken cancellationToken);

    /// The state, created at seq = 0 with a fresh epoch if missing. A sync-collection on an empty
    /// book needs an epoch to form its token, so it creates one; a pure read does not.
    Task<SyncState> ReadOrCreateStateAsync(Guid userId, CancellationToken cancellationToken);
}
