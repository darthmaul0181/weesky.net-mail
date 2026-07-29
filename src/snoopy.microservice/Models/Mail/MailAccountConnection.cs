using MailKit.Security;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// Everything needed to open the active account's connections. One structure, one code path:
/// the home server comes from appsettings, an external domain from the database.
/// </summary>
public sealed record MailAccountConnection(
    string AccountId,
    bool IsHomeServer,
    string ImapHost, int ImapPort, SecureSocketOptions ImapSecurity,
    string SmtpHost, int SmtpPort, SecureSocketOptions SmtpSecurity,
    string? SieveHost, int? SievePort,
    string Username, string Password)
{
    /// <summary>The wire spelling of the primary account ("" in the database, see StorageAccountId).</summary>
    public const string Primary = "primary";

    /// <summary>The database sentinel for this account ("" for primary, the GUID otherwise).</summary>
    public string StorageAccountId => AccountId == Primary ? string.Empty : AccountId;

    /// <summary>
    /// The staged-attachment namespace: user and account together, so two users' primary
    /// accounts never share files or quota. The only place the two dimensions may be composed.
    /// </summary>
    public static string StagedScope(User user, string accountId)
    {
        ArgumentNullException.ThrowIfNull(user);
        return $"{user.WebmailUid:N}:{accountId}";
    }

    /// <summary>This connection's staged-attachment namespace for <paramref name="user"/>.</summary>
    public string StagedScope(User user) => StagedScope(user, AccountId);

    /// <summary>Redacted: the generated ToString would print the password into any log line.</summary>
    public override string ToString() =>
        $"{AccountId} ({Username}, imap={ImapHost}:{ImapPort}, smtp={SmtpHost}:{SmtpPort})";
}
