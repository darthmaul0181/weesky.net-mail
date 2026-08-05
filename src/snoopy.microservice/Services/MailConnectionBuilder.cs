using System.Diagnostics.CodeAnalysis;
using MailKit.Security;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The single place a connection record is composed from an endpoint source, so the resolver that
/// serves a request and the probe that verifies a new password can never disagree on what a stored
/// value means. Endpoints only ever come from appsettings or from an admin-written domain row.
/// </summary>
internal static class MailConnectionBuilder
{
    /// <summary>The primary mailbox and every local shared one: endpoints from appsettings.</summary>
    public static MailAccountConnection Home(
        MailOptions home, string accountId, string username, MailCredential credential) =>
        new(accountId, IsHomeServer: true,
            home.ImapHost, home.ImapPort, home.ImapSecurity,
            home.SmtpHost, home.SmtpPort, home.SmtpSecurity,
            SieveHost: null, SievePort: null, username, credential);

    /// <summary>
    /// False when a stored security value is not one the admin screens can write, or names no
    /// transport security at all without <paramref name="allowCleartext"/> — the row is unusable,
    /// and the caller decides both what to log and what status to answer. The opt-in defaults to
    /// off so a caller that has no <see cref="MailOptions"/> to hand refuses the downgrade.
    /// </summary>
    public static bool TryExternal(
        ExternalDomain domain, string accountId, string username, MailCredential credential,
        [NotNullWhen(true)] out MailAccountConnection? connection, bool allowCleartext = false)
    {
        connection = null;

        // An OAuth domain the admin left half-configured is as unusable as one whose security
        // value does not parse, and answers the same way — the caller logs and 404s.
        if (domain.AuthMode is MailAuthMode.OAuth2 && !OAuthProviderConfig.TryFrom(domain, out _))
            return false;

        if (!TryParseSecurity(domain.ImapSecurity, allowCleartext, out var imapSecurity)
            || !TryParseSecurity(domain.SmtpSecurity, allowCleartext, out var smtpSecurity))
            return false;

        connection = new MailAccountConnection(
            accountId, IsHomeServer: false,
            domain.ImapHost, domain.ImapPort, imapSecurity,
            domain.SmtpHost, domain.SmtpPort, smtpSecurity,
            domain.SieveHost, domain.SievePort, username, credential);
        return true;
    }

    /// <summary>Only the three values the admin screens write, and None only under the opt-in.</summary>
    private static bool TryParseSecurity(string value, bool allowCleartext, out SecureSocketOptions security)
        => Enum.TryParse(value, out security)
           && security switch
           {
               SecureSocketOptions.StartTls or SecureSocketOptions.SslOnConnect => true,
               SecureSocketOptions.None => allowCleartext,
               _ => false
           };
}
