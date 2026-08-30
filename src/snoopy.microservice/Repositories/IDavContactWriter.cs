using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The third write gate, not a fourth path: a card received on <c>PUT</c> goes card →
/// <c>VCardProjector</c> → <c>ContactStore</c>'s projection replacement, exactly as the webmail
/// editor's and the import's writes do. What survives a replacement: <c>id</c>, <c>user_id</c>,
/// <c>is_favorite</c>, <c>source</c>. Everything else is a projection and is recomputed.
/// The request's Content-Type is never consulted: the body is the only judge.
/// </summary>
public interface IDavContactWriter
{
    /// <summary>
    /// Creates or replaces the resource named <paramref name="davName"/>. Archives whatever it
    /// replaces, advances the sequence, and lifts any tombstone on that name — all in one
    /// transaction. Never throws for a refusable card: each refusal comes back as its own
    /// <see cref="DavWriteStatus"/>. With <paramref name="createOnly"/> — the request only
    /// consented to create (If-None-Match: *) — a name already holding a visible resource is
    /// refused as <see cref="DavWriteStatus.AlreadyExists"/> INSIDE the gate, so the loser of a
    /// creation race writes nothing: without it the race's replay would replace the winner and
    /// hand the loser a 412 for a write that actually happened. <paramref name="ifMatch"/> is the
    /// raw If-Match header when the request carried one: the controller's own evaluation runs
    /// before any lock, so the decisive comparison is re-run here, under the state lock, against
    /// a fresh read — refusing as <see cref="DavWriteStatus.PreconditionFailed"/> the commit that
    /// landed in between, which is exactly the overwrite If-Match exists to prevent.
    /// </summary>
    Task<DavWriteOutcome> PutAsync(
        Guid userId, string davName, string card, CancellationToken cancellationToken,
        bool createOnly = false, string? ifMatch = null);

    /// <summary>
    /// Deletes it, archives its card and places a tombstone. <paramref name="ifMatch"/> guards it
    /// the way it guards <see cref="PutAsync"/>: re-compared under the state lock, so the version
    /// it protects cannot be the one a concurrent replacement just stored.
    /// </summary>
    Task<DavWriteOutcome> DeleteAsync(
        Guid userId, string davName, CancellationToken cancellationToken, string? ifMatch = null);

    /// <summary>
    /// Archives a body refused on a precondition, under the <c>Rejected</c> cause. Opens no state
    /// transaction and takes no rank: nothing visible to the protocol changed, and the 412 path
    /// must wake no client. Answers false when the deduplication window dropped it, or when the
    /// body outweighs what a revision may store.
    /// </summary>
    Task<bool> ArchiveRejectedAsync(
        Guid userId, string davName, string card, CancellationToken cancellationToken);
}
