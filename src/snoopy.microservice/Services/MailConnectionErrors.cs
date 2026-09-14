namespace weesky.Snoopy.Microservice.Services;

/// <summary>What <see cref="MailConnectionFactory{TClient,TSession}"/> answers when a session does not
/// open. Stable: the service account's connection test maps each one to a code the admin screen reads.</summary>
internal static class MailConnectionErrors
{
    public const string NotConfigured = "Mail service is not configured";
    public const string Unreachable = "Unable to connect to the mail service";
    public const string AuthenticationFailed = "Mail authentication failed";
    public const string AuthenticationUnsupported = "The mail service offers no supported authentication";
    public const string SecureChannelFailed = "Unable to establish a secure connection to the mail service";
    public const string TimedOut = "The mail service did not answer in time";
}
