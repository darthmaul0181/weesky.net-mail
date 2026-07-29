using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One webmail account, keyed by a surrogate GUID. The three preference tables reference this
/// row (FK, ON DELETE CASCADE); email is the natural key looked up at login and refreshed on
/// rename. Never mirrors a dovecot row structurally — this table lives only in snoopy_webmail.
/// </summary>
[Table("users")]
public sealed class WebmailUser
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Canonical (trimmed, lower-case); the table collates in binary.</summary>
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Rotated whenever the account's sessions must be cut. Every JWT carries the value that was
    /// current when it was issued, so one write here revokes every token for this account at once
    /// — which is the only way a password change can actually sign the other devices out.
    /// </summary>
    [Column("security_stamp")]
    public Guid SecurityStamp { get; set; }

    /// <summary>
    /// PBKDF2 salt of the key encrypting this user's connected-account passwords. Null until the
    /// first login after the migration generates one; never leaves the backend.
    /// </summary>
    [Column("kdf_salt")]
    public byte[]? KdfSalt { get; set; }

    [Column("creation_date")]
    public DateTime CreationDate { get; set; }

    [Column("last_login_date")]
    public DateTime? LastLoginDate { get; set; }
}
