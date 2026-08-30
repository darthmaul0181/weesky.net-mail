namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// The failure counter of the synchronisation scheme, as its callers see it. Public only because
/// a public controller cannot take an <c>internal</c> parameter (CS0051) — the implementation
/// stays internal, the same shape <see cref="IDavAuthenticationCache"/>, <c>IDavContactReader</c>
/// and <c>IDavContactWriter</c> already have.
///
/// <para>Every <c>identifier</c> is canonicalised by the throttle itself, trimmed and lower-cased,
/// so a caller spelling one differently still names the same key.</para>
/// </summary>
public interface IAuthAttemptThrottle
{
    /// <summary>
    /// Runs before anything is read, and before the digest is compared: past the threshold the
    /// correct secret is refused too, which is what <see cref="ForgetIdentifier"/> exists for.
    /// </summary>
    bool IsBlocked(string identifier, string? address, out TimeSpan retryAfter);

    void RecordFailure(string identifier, string? address);

    /// <summary>Clears the identifier's count, and only it.</summary>
    void RecordSuccess(string identifier);

    /// <summary>
    /// Clears the identifier's count without an authentication having succeeded: the caller just
    /// proved its identity with a JWT, a factor this throttle does not guard, and a regeneration
    /// puts every configured device into the failure loop that blocks the key.
    ///
    /// <para>The address key is deliberately left standing. Clearing it would let anyone sharing
    /// the victim's /64 unblock themselves by making a third party regenerate — which also means
    /// this does not always unblock the caller, whose own address may still be over the threshold.
    /// That asymmetry is the design, not an oversight.</para>
    /// </summary>
    void ForgetIdentifier(string identifier);
}
