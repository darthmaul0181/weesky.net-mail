using MailKit.Security;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// The home-server connection the way the controller builds it: <see cref="HomeOptions"/> feeds
/// the options monitor under test, and <see cref="Primary"/> is the value-equal connection the
/// mocks can match exactly.
/// </summary>
internal static class TestConnections
{
    public static MailOptions HomeOptions() => new()
    {
        ImapHost = "imap.home.test",
        ImapPort = 143,
        SmtpHost = "smtp.home.test",
        SmtpPort = 587
    };

    public static MailAccountConnection Primary(string email, MailCredential credential)
    {
        var options = HomeOptions();
        return new MailAccountConnection(
            MailAccountConnection.Primary, IsHomeServer: true,
            options.ImapHost, options.ImapPort, options.ImapSecurity,
            options.SmtpHost, options.SmtpPort, options.SmtpSecurity,
            SieveHost: null, SievePort: null, email, credential);
    }

    /// <summary>The overwhelmingly common case in the suite: a password mailbox.</summary>
    public static MailAccountConnection Primary(string email, string password) =>
        Primary(email, new PasswordCredential(password));

    /// <summary>A connected account on an external domain: its own endpoints and its own login.</summary>
    public static MailAccountConnection Connected(string accountId, string email, MailCredential credential) =>
        new(accountId, IsHomeServer: false,
            "imap.external.test", 993, SecureSocketOptions.SslOnConnect,
            "smtp.external.test", 465, SecureSocketOptions.SslOnConnect,
            SieveHost: null, SievePort: null, email, credential);

    public static MailAccountConnection Connected(string accountId, string email, string password) =>
        Connected(accountId, email, new PasswordCredential(password));

    /// <summary>The same external account on a domain the admin gave a Sieve endpoint.</summary>
    public static MailAccountConnection ConnectedWithSieve(string accountId, string email, MailCredential credential) =>
        Connected(accountId, email, credential) with { SieveHost = "sieve.external.test", SievePort = 4190 };

    public static MailAccountConnection ConnectedWithSieve(string accountId, string email, string password) =>
        ConnectedWithSieve(accountId, email, new PasswordCredential(password));

    /// <summary>A shared mailbox on our own server: a connected account still on the home endpoints.</summary>
    public static MailAccountConnection ConnectedLocal(string accountId, string email, MailCredential credential) =>
        Primary(email, credential) with { AccountId = accountId };

    public static MailAccountConnection ConnectedLocal(string accountId, string email, string password) =>
        ConnectedLocal(accountId, email, new PasswordCredential(password));
}
