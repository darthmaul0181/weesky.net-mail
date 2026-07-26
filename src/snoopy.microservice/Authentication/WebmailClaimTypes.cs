namespace weesky.Snoopy.Microservice.Authentication;

/// <summary>Custom JWT claim types. Upn/Dns come from System.Security.Claims; this one is ours.</summary>
public static class WebmailClaimTypes
{
    public const string Uid = "webmail_uid";

    /// <summary>The account's revocation stamp. A token without it is refused, never trusted.</summary>
    public const string Stamp = "webmail_stamp";
}
