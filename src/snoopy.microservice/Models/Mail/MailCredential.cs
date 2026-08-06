namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// How this application proves an identity to a mail server. Closed: the private protected
/// constructor keeps the two cases exhaustive, so a switch over them cannot silently grow a
/// third that nobody handled.
/// </summary>
public abstract record MailCredential
{
    private protected MailCredential() { }

    /// <summary>Sealed, not merely overridden: a record generates its own ToString unless the
    /// base has sealed it, and the generated one prints the secret.</summary>
    public sealed override string ToString() => GetType().Name;
}

/// <summary>A mailbox password, replayed on every login.</summary>
public sealed record PasswordCredential(string Password) : MailCredential;

/// <summary>A short-lived OAuth 2.0 access token, presented over SASL XOAUTH2.</summary>
public sealed record OAuthCredential(string AccessToken) : MailCredential;
