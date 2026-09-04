using MailKit.Security;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// What authenticated a pooled connection — never the account id the URL named. Transport
/// security is part of it so a domain an admin tightened never reuses a socket opened under the
/// old policy.
/// </summary>
internal readonly record struct PoolKey(
    string Host, int Port, SecureSocketOptions Security, string Username, string Fingerprint)
{
    public static PoolKey From(MailAccountConnection connection, CredentialFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(fingerprint);
        return new PoolKey(
            connection.ImapHost, connection.ImapPort, connection.ImapSecurity,
            connection.Username, fingerprint.Of(connection.Credential));
    }

    /// <summary>Log-safe: the fingerprint is derived from a password and never printed.</summary>
    public override string ToString() => $"{Username}@{Host}:{Port} ({Security})";
}
