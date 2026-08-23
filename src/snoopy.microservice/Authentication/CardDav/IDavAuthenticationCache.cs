namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>An authenticated synchronisation caller, as one lookup resolved them.</summary>
public readonly record struct DavIdentity(Guid UserId, bool CardDavEnabled);

/// <summary>
/// What one instance already knows about a synchronisation authentication: the burst cache, and
/// the amortisation of <c>last_used_at</c>. Both live in memory, per instance, and both are
/// assumed as such — shared, they would cost the read they exist to avoid; lost on redeploy, they
/// cost one extra lookup and one extra write per user.
///
/// Every <c>identifier</c> below is the full address, already trimmed and lower-cased by the
/// caller — the same canonicalisation <c>WebmailUserStore</c> applies. The cache compares it
/// byte for byte and never compensates for a different casing.
/// </summary>
public interface IDavAuthenticationCache
{
    /// <summary>
    /// The identity resolved for this exact (identifier, secret fingerprint) pair, when it is
    /// still within the window. The fingerprint is never the clear secret, which does not survive
    /// the request.
    /// </summary>
    bool TryGet(string identifier, string fingerprint, out DavIdentity identity);

    void Store(string identifier, string fingerprint, DavIdentity identity);

    /// <summary>
    /// Drops the cached authentication for an account, so a regenerated or revoked secret stops
    /// working on this instance at once — the touch throttle survives, since it holds no secret
    /// to invalidate. On the others the window is the ceiling — the same trade sessions make.
    ///
    /// <para>The synchronisation switch must call this too, on enable as much as on disable, and
    /// account deletion with it. The entry carries <see cref="DavIdentity.CardDavEnabled"/> and the
    /// cache never consults the database, so one that outlives a switch movement answers with the
    /// state from before it for the rest of the window: a disabled account still served 200, and a
    /// re-enabled one still refused 403 while the screen says "on".</para>
    /// </summary>
    void Forget(string identifier);

    /// <summary>
    /// True roughly once an hour per account — the read-then-write below is not locked, so two
    /// concurrent callers can each observe true once. Called on every authenticated request, so
    /// answering true every time would be one write per PROPFIND for a column the screen renders
    /// in the relative past.
    /// </summary>
    bool ShouldTouch(Guid userId);
}
