namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>
    /// The five user-assignable roles. "inbox" is deliberately absent: INBOX is fixed by the
    /// IMAP protocol itself, so there is nothing to correct.
    /// </summary>
    public static class FolderRoles
    {
        public static readonly IReadOnlyList<string> All = ["sent", "drafts", "trash", "junk", "archive"];

        public static bool IsValid(string? role) => role != null && All.Contains(role);
    }

    /// <summary>One role as the Settings page sees it: what it resolves to, and why.</summary>
    public class FolderRoleEntry
    {
        public string Role { get; set; } = string.Empty;

        /// <summary>Resolved folder path, or null when no source provides one.</summary>
        public string? FolderPath { get; set; }

        /// <summary>"override", "specialUse" or "name". Null when the role is unresolved.</summary>
        public string? Provenance { get; set; }

        /// <summary>
        /// Set when the user's stored choice no longer matches a live folder. Kept and
        /// signalled, never auto-deleted — the row only dies by the user's hand — and it
        /// coexists with a discovery-resolved FolderPath (spec § 5.3).
        /// </summary>
        public StaleOverrideInfo? StaleOverride { get; set; }
    }

    public class StaleOverrideInfo
    {
        public string FolderPath { get; set; } = string.Empty;
    }

    public class SetFolderRoleRequest
    {
        public string? Role { get; set; }

        public string? FolderPath { get; set; }
    }
}
