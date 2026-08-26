using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

internal interface IContactSyncStore
{
    /// <summary>
    /// Advances the counter under the state row's own exclusive lock and answers the new rank.
    /// Creates the row at seq = 1 on a first increment — with a fresh epoch — when it was missing;
    /// 0 stays reserved for "never written" so an empty book's ctag answers 0. MUST be called
    /// inside a transaction the caller owns, and FIRST — before any contact row is touched.
    /// </summary>
    Task<ulong> NextSequenceAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The state as it stands, creating nothing. Null when the user has never had one — a getctag
    /// on an empty book answers 0 without writing.
    ///
    /// A caller that goes on to read the tombstones or the contact rows MUST hold one transaction
    /// of its own across this call and those reads — one InnoDB snapshot. A prune landing between
    /// them raises pruned_below and deletes what it covers, so the response would serve deletions
    /// under a watermark already stale: the client keeps a card for ever, with nothing to signal
    /// it. That is the hole pruned_below exists to close, reopened by a race.
    ///
    /// No runtime guard enforces it, unlike <see cref="NextSequenceAsync"/>: a getctag reads
    /// nothing but this counter, and that single-statement call is legitimate — a guard would
    /// forbid a correct use. The precondition is on the composition, not on the call.
    /// </summary>
    Task<SyncState?> ReadStateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The state, created at seq = 0 with a fresh epoch if missing. A sync-collection on an empty
    /// book needs an epoch to form its token, so it creates one; a pure read does not. Two callers
    /// racing the first create for the same user are both answered correctly: the loser's insert
    /// fails on the row the winner just committed, and it re-reads and returns that row instead of
    /// throwing.
    ///
    /// The same-snapshot precondition as <see cref="ReadStateAsync"/>, and here it binds in
    /// practice on every call: this overload exists for the caller that forms a token, and forming
    /// one means going on to read what the token covers. Its own create is atomic by itself, so
    /// the transaction is owed to that composition, not to the write.
    /// </summary>
    Task<SyncState> ReadOrCreateStateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the tombstone at (userId, davName) rather than inserting: the key is
    /// (user_id, dav_name), so a name deleted, recreated and deleted again lands on an existing
    /// row, and a bare INSERT would fail that second deletion on a duplicate key.
    /// </summary>
    Task PlaceTombstoneAsync(Guid userId, string davName, ulong sequence, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the tombstone at (userId, davName) if one exists. Quiet when it does not: most
    /// creates lift a name that was never buried.
    /// </summary>
    Task LiftTombstoneAsync(Guid userId, string davName, CancellationToken cancellationToken);

    /// <summary>
    /// Archives one revision. Answers false when the deduplication window for a repeatedly
    /// rejected body discarded it instead of writing it.
    /// </summary>
    Task<bool> ArchiveAsync(ContactRevision revision, CancellationToken cancellationToken);

    /// <summary>
    /// Raises the watermark and removes what it now covers, tombstones and revisions on their own
    /// clocks, all in one transaction.
    /// </summary>
    Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken);
}
