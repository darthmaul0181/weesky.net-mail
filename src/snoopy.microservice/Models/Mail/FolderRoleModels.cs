namespace weesky.Snoopy.Microservice.Models.Mail;

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
public sealed class FolderRoleEntry
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

/// <summary>
/// Why a stored override no longer holds. The client words its notice from this: a single
/// undifferentiated "stale" flag left it asserting the folder was renamed or deleted even
/// when the folder is right there, and the truth is that it cannot hold messages or that
/// something else already claimed it.
/// </summary>
public static class StaleOverrideReasons
{
    /// <summary>No live folder matches — deleted, renamed beyond reach, or its path reused.</summary>
    public const string Missing = "missing";

    /// <summary>The folder is there but is \NoSelect / \NonExistent: it cannot hold messages.</summary>
    public const string NotSelectable = "notSelectable";

    /// <summary>The folder is there and usable, but INBOX or a higher-priority override owns it.</summary>
    public const string FolderTaken = "folderTaken";
}

public sealed class StaleOverrideInfo
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>One of <see cref="StaleOverrideReasons"/>.</summary>
    public string Reason { get; set; } = StaleOverrideReasons.Missing;
}

public sealed class SetFolderRoleRequest
{
    public string? Role { get; set; }

    public string? FolderPath { get; set; }
}
