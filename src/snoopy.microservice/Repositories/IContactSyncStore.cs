using weesky.Snoopy.Microservice.Data.Preferences;
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

    /// Replaces the tombstone at (userId, davName) rather than inserting: the key is
    /// (user_id, dav_name), so a name deleted, recreated and deleted again lands on an existing
    /// row, and a bare INSERT would fail that second deletion on a duplicate key.
    Task PlaceTombstoneAsync(Guid userId, string davName, ulong sequence, CancellationToken cancellationToken);

    /// Removes the tombstone at (userId, davName) if one exists. Quiet when it does not: most
    /// creates lift a name that was never buried.
    Task LiftTombstoneAsync(Guid userId, string davName, CancellationToken cancellationToken);

    /// Archives one revision. Answers false when the deduplication window for a repeatedly
    /// rejected body discarded it instead of writing it.
    Task<bool> ArchiveAsync(ContactRevision revision, CancellationToken cancellationToken);

    /// Raises the watermark and removes what it now covers, tombstones and revisions on their own
    /// clocks, all in one transaction.
    Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken);
}
