namespace weesky.Scotty.Microservice.Authentication.Dav;

/// <summary>
/// The failure counter of the synchronisation scheme, as its callers see it. Public only because
/// a public controller cannot take an <c>internal</c> parameter (CS0051) — the implementation
/// stays internal, the same shape <see cref="IDavAuthenticationCache"/>, <c>IDavContactReader</c>
/// and <c>IDavContactWriter</c> already have.
///
/// <para>Counted per client address only. The secret is ~100 random bits, so a count per account
/// slows no guessing and only hands anyone who knows an address a way to lock its owner out.</para>
/// </summary>
public interface IAuthAttemptThrottle
{
    /// <summary>Runs before anything is read, and before the digest is compared.</summary>
    bool IsBlocked(string? address, out TimeSpan retryAfter);

    void RecordFailure(string? address);
}
