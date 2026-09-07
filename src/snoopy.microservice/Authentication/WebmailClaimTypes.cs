namespace weesky.Snoopy.Microservice.Authentication;

/// <summary>Custom JWT claim types. Upn/Dns come from System.Security.Claims; this one is ours.</summary>
public static class WebmailClaimTypes
{
    public const string Uid = "webmail_uid";

    /// <summary>The account's revocation stamp. A token without it is refused, never trusted.</summary>
    public const string Stamp = "webmail_stamp";

    /// <summary>"1" or "0", written by the synchronisation handler alone. Absent on a JWT
    /// principal: the webmail's own session is never a synchronising device, so a shape reading
    /// these treats absence as on.</summary>
    public const string CardDav = "webmail_carddav";

    public const string CalDav = "webmail_caldav";
}
