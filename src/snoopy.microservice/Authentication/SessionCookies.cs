using weesky.Snoopy.Microservice.Authentication.Models;

namespace weesky.Snoopy.Microservice.Authentication;

/// <summary>
/// Writes and clears the JWT cookie. Three places issue a session — the login, the sliding
/// renewal, and a password change re-issuing its own — and the cookie's flags are a security
/// property, so they are spelled out once rather than three times.
/// </summary>
internal static class SessionCookies
{
    public static void WriteAuthCookie(this HttpResponse response, TokenConstants constants, string token) =>
        response.Cookies.Append(constants.AuthCookieName, token, Options(
            DateTimeOffset.UtcNow.AddMinutes(constants.ExpiryInMinutes)));

    public static void ClearAuthCookie(this HttpResponse response, TokenConstants constants) =>
        response.Cookies.Delete(constants.AuthCookieName, Options(expires: null));

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Expires = expires
    };
}
