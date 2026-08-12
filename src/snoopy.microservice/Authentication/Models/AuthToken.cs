namespace weesky.Snoopy.Microservice.Authentication.Models;

/// <summary>
/// The issued session, as it travels inside the service. It is never a response body: the token
/// belongs to the HttpOnly cookie alone, and controllers answer
/// <see cref="Microservice.Models.LoginResponse"/>.
/// </summary>
public sealed class AuthToken
{
    /// <summary>
    /// Expiry in minutes
    /// </summary>
    public long ExpiresIn { get; set; }

    /// <summary>
    /// The Json Web Token used to authenticate the user.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The account this token was issued for, as the caller typed it — canonicalised
    /// (trimmed, lower-cased) by <c>IdentityResolver.Canonical</c> before the IMAP LOGIN, not
    /// looked up from a database row. Anything keyed on the account after a login has to use this
    /// one: it is the single spelling shared by the webmail store row and the credentials-cookie
    /// KDF salt, so a second spelling of the same mailbox would miss the row or derive the wrong key.
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
