namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>An authenticated synchronisation caller, as one lookup resolved them.</summary>
public readonly record struct DavIdentity(Guid UserId, bool CardDavEnabled);

/// <summary>
/// What one instance already knows about a synchronisation authentication: the burst cache, and
/// the amortisation of <c>last_used_at</c>. Both live in memory, per instance, and both are
/// assumed as such — shared, they would cost the read they exist to avoid; lost on redeploy, they
/// cost one extra lookup and one extra write per user.
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
    /// Drops what is known about an account, so a regenerated or revoked secret stops working on
    /// this instance at once. On the others the window is the ceiling — the same trade sessions make.
    /// </summary>
    void Forget(string identifier);

    /// <summary>
    /// True at most once an hour per account. Called on every authenticated request, so answering
    /// true every time would be one write per PROPFIND for a column the screen renders in the
    /// relative past.
    /// </summary>
    bool ShouldTouch(Guid userId);
}
