namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// What an admin may save as a mail endpoint — an external domain's servers, the calendar service
/// account's submission server. One rule set, so a host one screen accepts is never refused by
/// the other. Each answers null when the value is acceptable, the reason otherwise.
/// </summary>
internal static class MailEndpointRules
{
    internal const string CleartextRefused = "security cannot be None: set Mail:AllowCleartext to accept an unencrypted endpoint";

    private static readonly HashSet<string> EncryptedSecurities = ["StartTls", "SslOnConnect"];

    public static string? ValidateHost(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 255) return "Host must be between 1 and 255 characters";
        return Uri.CheckHostName(host) == UriHostNameType.Unknown ? "Host is not a valid hostname or IP address" : null;
    }

    /// <summary>Exact literals, not <c>Enum.TryParse</c>, which takes numbers too. None is refused here,
    /// at save time, rather than failing on every use with nothing on screen saying why.</summary>
    public static string? ValidateSecurity(string? security, bool allowCleartext) => security switch
    {
        _ when security is not null && EncryptedSecurities.Contains(security) => null,
        "None" when allowCleartext => null,
        "None" => CleartextRefused,
        _ => "security must be exactly one of None, StartTls, SslOnConnect"
    };
}
