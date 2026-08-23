namespace weesky.Snoopy.Microservice.Authentication.CardDav;

public static class CardDavAuthenticationDefaults
{
    public const string AuthenticationScheme = "CardDav";

    /// <summary>
    /// A Basic challenge without a realm makes Thunderbird re-ask for credentials at every launch,
    /// and the realm is a keychain key on the client side: this string must never vary between
    /// deployments.
    /// </summary>
    public const string Realm = "weesky CardDAV";

    /// <summary>
    /// The named policy the /dav routes carry (slice 4c-ii). It names this scheme alone, so the
    /// challenge is Basic and only Basic; the JWT still authenticates, through the handler.
    /// </summary>
    public const string PolicyName = "Dav";
}
