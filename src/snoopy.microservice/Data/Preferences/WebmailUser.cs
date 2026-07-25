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

    [Column("creation_date")]
    public DateTime CreationDate { get; set; }

    [Column("last_login_date")]
    public DateTime? LastLoginDate { get; set; }
}
