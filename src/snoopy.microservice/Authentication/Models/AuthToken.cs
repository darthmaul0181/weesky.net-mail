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
}
