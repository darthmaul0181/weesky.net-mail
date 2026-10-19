namespace weesky.Scotty.Microservice.Authentication;

/// <summary>
/// Ties a consent to the browser that started it (RFC 6749 §10.12). The state alone names the
/// webmail user, not the browser: a user could hand their own authorization URL to somebody else
/// and collect that person's mailbox at Complete. Lax, because the provider's redirect is a
/// cross-site top-level GET, which Strict would strip; one cookie per state, so two consents in
/// two tabs never overwrite each other's.
/// </summary>
internal static class OAuthBindingCookies
{
    private const string Prefix = "scotty_oauth_";

    /// <param name="callbackPath">the redirect URI's own path, so a proxy prefix still matches</param>
    public static void WriteOAuthBinding(
        this HttpResponse response, string state, string callbackPath, TimeSpan lifetime) =>
        response.Cookies.Append(Prefix + state, "1",
            Options(callbackPath, DateTimeOffset.UtcNow.Add(lifetime)));

    public static bool HoldsOAuthBinding(this HttpRequest request, string state) =>
        request.Cookies.ContainsKey(Prefix + state);

    public static void ClearOAuthBinding(this HttpResponse response, string state, string callbackPath) =>
        response.Cookies.Delete(Prefix + state, Options(callbackPath, expires: null));

    private static CookieOptions Options(string callbackPath, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = callbackPath,
        Expires = expires
    };
}
