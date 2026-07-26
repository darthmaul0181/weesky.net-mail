namespace weesky.Snoopy.Microservice.Authentication.Services;

/// <summary>
/// Decides whether the session a bearer token describes is still live, and cuts every session of
/// an account when asked.
///
/// A JWT is self-contained: once signed it is valid until it expires, and nothing about signing
/// out, changing a password or disabling an account reaches it. This is what closes that gap —
/// the account carries a stamp, every token carries the stamp that was current when it was
/// issued, and rotating the stored one leaves every token already out there unable to match.
/// </summary>
public interface ISessionGuard
{
    /// <summary>
    /// Whether the account still exists, is usable, and has not had its sessions revoked since the
    /// token was issued. Answered from a short-lived cache: this runs on every authenticated
    /// request, and the TTL is the ceiling on how long a revoked session keeps working.
    /// </summary>
    Task<bool> IsCurrentAsync(string email, Guid presentedStamp, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the cached state for an account, so a rotation takes effect immediately instead of
    /// at the end of the cache window. Called by whoever rotates the stamp.
    /// </summary>
    void Forget(string email);
}
