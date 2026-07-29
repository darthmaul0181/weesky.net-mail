namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// What the credentials cookie carries: the user's mail password, and the key encrypting their
/// connected-account passwords. The key costs 600k PBKDF2 iterations, far too much to pay per
/// request, so it is derived once at login and carried here for the rest of the session.
///
/// <see cref="Kek"/> is null for a v1 cookie still in circulation — derived on demand and
/// re-issued as v2 by the resolver.
/// </summary>
public sealed record MailCredentialPayload(string Password, byte[]? Kek)
{
    /// <summary>Redacted: the generated ToString would print the password into any log line.</summary>
    public override string ToString() => $"MailCredentialPayload (v{(Kek is null ? 1 : 2)})";
}
