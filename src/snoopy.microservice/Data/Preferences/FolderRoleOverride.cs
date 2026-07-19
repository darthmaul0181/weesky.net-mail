using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences
{
    /// <summary>
    /// One user-chosen folder-role assignment. Absence of a row means the role falls back to
    /// discovery (SPECIAL-USE, then name matching): the override is a correction layer, not a
    /// replacement, so a freshly provisioned mailbox needs no rows at all.
    /// </summary>
    [Table("folder_role_overrides")]
    public class FolderRoleOverride
    {
        [Column("account_id")]
        public string AccountId { get; set; } = string.Empty;

        /// <summary>Stable enum value ("trash", never a localised word). See FolderRoles.All.</summary>
        [Column("role")]
        public string Role { get; set; } = string.Empty;

        /// <summary>Always stored: the one identifier IMAP guarantees on every server.</summary>
        [Column("folder_path")]
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>Staleness guard: catches a path reused by a different folder.</summary>
        [Column("uid_validity")]
        public ulong UidValidity { get; set; }

        /// <summary>RFC 8474 MAILBOXID — an optional aid, never the key.</summary>
        [Column("mailbox_id")]
        public string? MailboxId { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
