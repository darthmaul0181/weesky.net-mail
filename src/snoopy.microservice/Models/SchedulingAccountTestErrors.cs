using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>The stable codes a connection test answers; the admin screen turns each into a sentence.</summary>
internal static class SchedulingAccountTestErrors
{
    public const string SmtpUnreachable = "smtp_unreachable";
    public const string SmtpAuthFailed = "smtp_auth_failed";
    public const string SmtpAuthUnsupported = "smtp_auth_unsupported";
    public const string SmtpTlsFailed = "smtp_tls_failed";
    public const string SmtpTimeout = "smtp_timeout";
    public const string PasswordUnreadable = "password_unreadable";
    public const string StoredAccountInvalid = "stored_account_invalid";

    public static string FromConnectionError(string error) => error switch
    {
        MailConnectionErrors.AuthenticationFailed => SmtpAuthFailed,
        MailConnectionErrors.AuthenticationUnsupported => SmtpAuthUnsupported,
        MailConnectionErrors.SecureChannelFailed => SmtpTlsFailed,
        MailConnectionErrors.TimedOut => SmtpTimeout,
        _ => SmtpUnreachable
    };
}
