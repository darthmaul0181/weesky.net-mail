using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Dav;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// <see cref="IDavContactWriter"/>'s twin, keyed by calendar: a file received on <c>PUT</c> goes
/// through <c>CalendarEventStore.ApplyIcsAsync</c>, the one place <c>ics_raw</c> and its index are
/// written, exactly as the editor's and the import's writes do. The file is stored VERBATIM — no
/// VTIMEZONE added, no DTSTAMP rewritten, no UID inserted — so the ETag is always the sent bytes'.
/// The request's Content-Type is never consulted: the body is the only judge. Public because the
/// controller, which can only be public, injects it.
/// </summary>
public interface IDavCalendarWriter
{
    /// <summary>
    /// Creates or replaces the resource named <paramref name="davName"/> in one calendar. Judges
    /// the five guards of <c>IcsGuards.CheckAll</c> first, then, under the collection's state lock:
    /// <paramref name="ifMatch"/> re-compared, <paramref name="createOnly"/> re-judged, a UID held
    /// by ANOTHER name of the SAME calendar or changed under this very name both refused as
    /// <see cref="DavWriteStatus.UidConflict"/> with the relevant href, the ceiling counted, the
    /// replaced bytes archived, any tombstone on the name lifted. Never throws for a refusable
    /// file: each refusal comes back as its own
    /// <see cref="DavWriteStatus"/>, an invalid one carrying the precondition it broke.
    /// <paramref name="cause"/> names the door for the archive — the webmail's invitation writes
    /// pass <c>Webmail</c>.
    /// </summary>
    Task<DavWriteOutcome> PutAsync(Guid userId, Guid calendarId, string davName, string ics,
        CancellationToken cancellationToken, bool createOnly = false, string? ifMatch = null,
        RevisionCause cause = RevisionCause.Put);

    /// <summary>
    /// Deletes it, archives its file and places a tombstone. <paramref name="ifMatch"/> guards it
    /// the way it guards <see cref="PutAsync"/>: re-compared under the state lock, so the version
    /// it protects cannot be the one a concurrent replacement just stored.
    /// </summary>
    Task<DavWriteOutcome> DeleteAsync(Guid userId, Guid calendarId, string davName,
        CancellationToken cancellationToken, string? ifMatch = null);

    /// <summary>
    /// Empties the calendar: every resource archived and buried, one transaction and one rank per
    /// batch of <c>CalendarStore.DeleteBatch</c>, a tombstone per resource. Answers
    /// <see cref="DavWriteStatus.Deleted"/> on an already-empty calendar too, where it takes NO
    /// rank. A lock race answers <see cref="DavWriteStatus.Busy"/>; the batches already committed
    /// stay buried, which is what the client's retry finishes rather than undoes.
    /// </summary>
    Task<DavWriteOutcome> DeleteAllAsync(Guid userId, Guid calendarId, CancellationToken cancellationToken);

    /// <summary>
    /// Archives a body refused on a precondition, under the <c>Rejected</c> cause. Opens no state
    /// transaction and takes no rank: nothing visible to the protocol changed, and the 412 path
    /// must wake no client. Answers false when the body outweighs what a revision may store, or
    /// when a lock race dropped it.
    /// </summary>
    Task<bool> ArchiveRejectedAsync(Guid userId, Guid calendarId, string davName, string ics,
        CancellationToken cancellationToken);
}
