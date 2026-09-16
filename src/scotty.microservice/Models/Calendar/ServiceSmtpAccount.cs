using MailKit.Security;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Services;

namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>The service account's submission endpoint, its password decrypted: what the queue
/// and the connection test open a session with.</summary>
public sealed record ServiceSmtpAccount(string Host, int Port, SecureSocketOptions Security, string Login, string Password)
{
    /// <summary>Null when the stored row cannot be used: its password no longer decrypts
    /// (<paramref name="passwordReadable"/> false), or its security is not one the admin screen writes.</summary>
    internal static ServiceSmtpAccount? Open(SchedulingServiceAccount row, ISecretProtector protector, out bool passwordReadable)
    {
        var password = protector.Unprotect(row.PasswordCipher);
        passwordReadable = password is not null;
        return password is not null && MailConnectionBuilder.TryParseSecurity(row.Security, allowCleartext: true, out var security)
            ? new ServiceSmtpAccount(row.Host, row.Port, security, row.Login, password)
            : null;
    }

    internal MailAccountConnection ToConnection() => new("scheduling", IsHomeServer: true,
        string.Empty, 0, SecureSocketOptions.None, Host, Port, Security,
        SieveHost: null, SievePort: null, Login, new PasswordCredential(Password));

    /// <summary>Redacted: the generated ToString would print the password into any log line.</summary>
    public override string ToString() => $"{Login} ({Host}:{Port}, {Security})";
}
