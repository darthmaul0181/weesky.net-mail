using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// <see cref="IContactSyncStore"/>'s twin, keyed by calendar rather than by user: CalDAV syncs each
/// collection on its own, so every counter, tombstone and watermark here belongs to one collection.
/// </summary>
internal interface ICalendarSyncStore
{
    /// <summary>
    /// Advances the collection's counter under its own exclusive lock and answers the new rank.
    /// Creates the row at seq = 1 when it was missing; 0 stays reserved for "never written". MUST
    /// be called inside a transaction the caller owns, and FIRST — before any row is touched.
    /// </summary>
    Task<ulong> NextSequenceAsync(Guid calendarId, CancellationToken cancellationToken);

    /// <summary>
    /// The state row of a collection being created, at seq = 0 with a fresh epoch, inside that
    /// creation's own transaction (décision 2): a collection with no counter has no ctag, and a
    /// client polling it before its first write would read one out of nothing.
    /// </summary>
    Task CreateStateAsync(Guid calendarId, CancellationToken cancellationToken);

    /// <summary>
    /// The state as it stands, creating nothing. A caller that goes on to read the tombstones or
    /// the event rows MUST hold one transaction across this call and those reads — one snapshot:
    /// a prune landing between them raises the watermark under a response already formed.
    /// </summary>
    Task<SyncState?> ReadStateAsync(Guid calendarId, CancellationToken cancellationToken);

    /// <summary>Replaces the tombstone at (calendarId, davName) rather than inserting: a name
    /// deleted, recreated and deleted again lands on an existing row.</summary>
    Task PlaceTombstoneAsync(
        Guid calendarId, string davName, ulong rank, CancellationToken cancellationToken);

    /// <summary>Removes the tombstone at (calendarId, davName) if one exists; quiet otherwise.</summary>
    Task LiftTombstoneAsync(Guid calendarId, string davName, CancellationToken cancellationToken);

    /// <summary>
    /// Archives one set of bytes. The hash is computed here and nowhere else — a hash a caller
    /// computes is a hash a caller will forget.
    /// </summary>
    Task ArchiveAsync(
        Guid userId, Guid? calendarId, Guid? eventId, string? uid, string? davName, string icsRaw,
        RevisionCause cause, CancellationToken cancellationToken);

    /// <summary>Raises the watermarks and removes what they now cover, tombstones and revisions on
    /// their own clocks, all in one transaction.</summary>
    Task<PruneOutcome> PruneAsync(
        DateTime tombstonesBefore, DateTime revisionsBefore, CancellationToken cancellationToken);
}
