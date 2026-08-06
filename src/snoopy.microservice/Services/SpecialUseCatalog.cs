using MailKit;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The SPECIAL-USE catalogue: which role a server flag or a well-known folder name maps to,
/// and the whole-list resolution that assigns each discovered role to exactly one folder.
/// Pure over its inputs — no connection, no session — which is what lets
/// <see cref="FolderRoleResolver"/> stay pure too.
/// </summary>
internal static class SpecialUseCatalog
{
    /// <summary>
    /// Assigns discovered roles, each to at most one folder — and each folder to at most
    /// one role.
    /// </summary>
    /// <remarks>
    /// Two claim sets, not one. Claimed roles keep a mailbox holding both "Drafts" and
    /// "Brouillons" from ending up with two drafts folders. Claimed folders keep one
    /// folder from holding two roles — a folder flagged \Sent but named "Trash" used to
    /// claim both, which is undecidable to display. Callers may seed both sets: the role
    /// resolver runs user overrides first and hands discovery only the leftovers.
    ///
    /// A non-selectable folder never holds a role, in either pass. The ordinary shape
    /// that makes this load-bearing: "Archive" exists only as a \NoSelect container for
    /// "Archive/2024" and "Archive/2025". Letting the container win the name pass stamped
    /// a role on a mailbox that cannot hold a message and locked the real archive folder
    /// out of it. Level 1 already refuses a non-selectable override target; the same rule
    /// has to hold here.
    /// </remarks>
    public static IReadOnlyDictionary<string, SpecialUseAssignment> ResolveSpecialUses(
        IEnumerable<(string Path, string Name, string? AttributeRole, bool Selectable)> folders,
        IEnumerable<string>? claimedRoles = null,
        IEnumerable<string>? claimedFolders = null)
    {
        var candidates = folders.ToList();
        var roles = new HashSet<string>(claimedRoles ?? [], StringComparer.Ordinal);
        var taken = new HashSet<string>(claimedFolders ?? [], StringComparer.Ordinal);
        var result = new Dictionary<string, SpecialUseAssignment>(StringComparer.Ordinal);

        foreach (var folder in candidates)
        {
            if (!folder.Selectable) continue;
            if (folder.AttributeRole is not { } role) continue;
            if (taken.Contains(folder.Path)) continue;

            if (!roles.Contains(role))
            {
                roles.Add(role);
                result[folder.Path] = new SpecialUseAssignment(role, SpecialUseAssignment.FromFlag);
            }

            // Taken whether it won the role or lost it. A folder the server flagged is
            // never a candidate for name guessing: losing the race to another \Sent folder
            // must leave it with no role at all, because showing it as "Trash" on the
            // strength of its name would let a guess contradict what the server declared.
            taken.Add(folder.Path);
        }

        foreach (var folder in candidates)
        {
            if (!folder.Selectable) continue;

            if (SpecialUseFromName(folder.Name) is { } role && !roles.Contains(role) && !taken.Contains(folder.Path))
            {
                roles.Add(role);
                taken.Add(folder.Path);
                result[folder.Path] = new SpecialUseAssignment(role, SpecialUseAssignment.FromName);
            }
        }

        return result;
    }

    public static string? SpecialUseFromAttributes(FolderAttributes attributes, bool isInbox)
    {
        if (isInbox) return "inbox";

        if ((attributes & FolderAttributes.Sent) != 0) return "sent";
        if ((attributes & FolderAttributes.Drafts) != 0) return "drafts";
        if ((attributes & FolderAttributes.Trash) != 0) return "trash";
        if ((attributes & FolderAttributes.Junk) != 0) return "junk";
        if ((attributes & FolderAttributes.Archive) != 0) return "archive";

        return null;
    }

    /// <summary>
    /// Last-resort guess for servers that advertise no SPECIAL-USE. Covers the localised
    /// names a mail client creates when it, not the server, provisioned the folders.
    /// </summary>
    public static string? SpecialUseFromName(string name) => name.ToLowerInvariant() switch
    {
        "inbox" => "inbox",
        "sent" or "sent messages" or "sent items"
            or "envoyés" or "éléments envoyés" or "messages envoyés" => "sent",
        "drafts" or "draft" or "brouillons" => "drafts",
        "trash" or "deleted" or "deleted messages" or "deleted items"
            or "corbeille" or "éléments supprimés" => "trash",
        "junk" or "spam" or "junk e-mail"
            or "courrier indésirable" or "indésirables" or "pourriel" => "junk",
        "archive" or "archives" => "archive",
        _ => null
    };
}
