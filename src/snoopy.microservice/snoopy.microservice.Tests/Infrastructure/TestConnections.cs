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

    public static MailAccountConnection Primary(string email, string password)
    {
        var options = HomeOptions();
        return new MailAccountConnection(
            MailAccountConnection.Primary, IsHomeServer: true,
            options.ImapHost, options.ImapPort, options.ImapSecurity,
            options.SmtpHost, options.SmtpPort, options.SmtpSecurity,
            SieveHost: null, SievePort: null, email, password);
    }

    /// <summary>A connected account on an external domain: its own endpoints and its own login.</summary>
    public static MailAccountConnection Connected(string accountId, string email, string password) =>
        new(accountId, IsHomeServer: false,
            "imap.external.test", 993, SecureSocketOptions.SslOnConnect,
            "smtp.external.test", 465, SecureSocketOptions.SslOnConnect,
            SieveHost: null, SievePort: null, email, password);

    /// <summary>The same external account on a domain the admin gave a Sieve endpoint.</summary>
    public static MailAccountConnection ConnectedWithSieve(string accountId, string email, string password) =>
        Connected(accountId, email, password) with { SieveHost = "sieve.external.test", SievePort = 4190 };

    /// <summary>A shared mailbox on our own server: a connected account still on the home endpoints.</summary>
    public static MailAccountConnection ConnectedLocal(string accountId, string email, string password) =>
        Primary(email, password) with { AccountId = accountId };
}
