namespace weesky.Snoopy.Microservice.Authentication.Models;

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
