using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// The role resolution chain (spec § 4.1): user overrides, then SPECIAL-USE flags, then
    /// name matching — each level filling only what the previous one left. Level 2, an
    /// admin-set domain default, was evaluated and rejected; its slot stays vacant on purpose.
    ///
    /// Pure over its inputs — the tree and the stored overrides. No IMAP, no database, no
    /// HTTP: that is what makes every staleness rule testable without a server.
    /// </summary>
    public static class FolderRoleResolver
    {
        public static FolderRoleResolution Resolve(
            IReadOnlyList<MailFolderNode> tree,
            IReadOnlyList<FolderRoleOverride> overrides)
        {
            var flat = new List<MailFolderNode>();
            Flatten(tree, flat);

            var byPath = flat.ToDictionary(n => n.Path, StringComparer.Ordinal);
            var byMailboxId = flat.Where(n => n.MailboxId != null)
                                  .ToDictionary(n => n.MailboxId!, StringComparer.Ordinal);

            // The chain tracks BOTH sets. Tracking roles alone is the natural bug: it passes
            // every test except the one where an override takes a flagged folder, whose flag
            // would then claim a second role for it.
            var claimedRoles = new HashSet<string>(StringComparer.Ordinal);
            var claimedFolders = new HashSet<string>(StringComparer.Ordinal);
            var roleByPath = new Dictionary<string, string>(StringComparer.Ordinal);

            // INBOX first, before any override: it is fixed by the protocol itself, so no
            // stored row may displace it or claim its folder.
            var inbox = flat.FirstOrDefault(n => n.AttributeRole == "inbox");
            if (inbox != null)
            {
                roleByPath[inbox.Path] = "inbox";
                claimedRoles.Add("inbox");
                claimedFolders.Add(inbox.Path);
            }

            // Level 1: user overrides, walked in FolderRoles.All order so ties are deterministic.
            var entries = new List<FolderRoleEntry>();
            foreach (var role in FolderRoles.All)
            {
                var entry = new FolderRoleEntry { Role = role };
                entries.Add(entry);

                var @override = overrides.FirstOrDefault(o => o.Role == role);
                if (@override == null) continue;

                var node = ResolveOverride(@override, byPath, byMailboxId);
                if (node != null && node.Selectable && !claimedFolders.Contains(node.Path))
                {
                    claimedRoles.Add(role);
                    claimedFolders.Add(node.Path);
                    roleByPath[node.Path] = role;
                    entry.FolderPath = node.Path;
                    entry.Provenance = "override";
                }
                else
                {
                    // Kept and signalled (§ 5.3), never auto-deleted; discovery below may
                    // still fill the role, and both facts then coexist in the entry.
                    entry.StaleOverride = new StaleOverrideInfo { FolderPath = @override.FolderPath };
                }
            }

            // Levels 3 and 4: discovery over whatever roles and folders the overrides left.
            var discovered = ImapSession.ResolveSpecialUses(
                flat.Select(n => (n.Path, n.Name, n.AttributeRole)),
                claimedRoles,
                claimedFolders);

            foreach (var (path, assignment) in discovered)
            {
                roleByPath[path] = assignment.Role;

                var entry = entries.FirstOrDefault(e => e.Role == assignment.Role);
                if (entry != null && entry.Provenance == null)
                {
                    entry.FolderPath = path;
                    entry.Provenance = assignment.Source;
                }
            }

            return new FolderRoleResolution { Roles = entries, RoleByPath = roleByPath };
        }

        /// <summary>
        /// A stored override resolves by MAILBOXID when both sides carry one — immune to
        /// renames, ours and other clients' alike — and otherwise by path guarded by
        /// UIDVALIDITY, which catches the one failure mode that lies rather than degrades: a
        /// deleted folder whose path was reused by a different one.
        /// </summary>
        private static MailFolderNode? ResolveOverride(
            FolderRoleOverride @override,
            IReadOnlyDictionary<string, MailFolderNode> byPath,
            IReadOnlyDictionary<string, MailFolderNode> byMailboxId)
        {
            if (@override.MailboxId != null && byMailboxId.TryGetValue(@override.MailboxId, out var byId))
                return byId;

            return byPath.TryGetValue(@override.FolderPath, out var node)
                   && node.UidValidity == @override.UidValidity
                ? node
                : null;
        }

        private static void Flatten(IReadOnlyList<MailFolderNode> nodes, List<MailFolderNode> into)
        {
            foreach (var node in nodes)
            {
                into.Add(node);
                Flatten(node.Children, into);
            }
        }
    }

    public sealed class FolderRoleResolution
    {
        /// <summary>Exactly the five assignable roles, in FolderRoles.All order.</summary>
        public required IReadOnlyList<FolderRoleEntry> Roles { get; init; }

        /// <summary>
        /// Authoritative path→role map, "inbox" included. GET /Folders stamps this onto the
        /// tree's SpecialUse, so the client always sees the chain's output.
        /// </summary>
        public required IReadOnlyDictionary<string, string> RoleByPath { get; init; }
    }
}
