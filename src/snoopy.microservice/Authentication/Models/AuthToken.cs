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
    /// The account this token was issued for, as the database spells it — not as the caller typed
    /// it. Anything keyed on the account after a login has to use this one: a row looked up under
    /// the caller's spelling can miss, and the caller only learns of it much later, through
    /// whatever that row was holding.
    /// </summary>
    public string Email { get; set; } = string.Empty;
}
