using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>A folder as the client sees it. Children are nested, not flattened.</summary>
    public class MailFolderNode
    {
        /// <summary>
        /// Full IMAP path, separator included. Opaque to the client: the hierarchy separator
        /// is whatever the server uses, so the client must never parse or build one.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Leaf name for display.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Well-known role when the server advertises one, or when a name fallback matched:
        /// "inbox", "sent", "drafts", "trash", "junk", "archive". Null for ordinary folders.
        /// </summary>
        public string? SpecialUse { get; set; }

        /// <summary>False when the folder holds no messages (a container-only node).</summary>
        public bool Selectable { get; set; } = true;

        /// <summary>Whether the user subscribed to this folder — drives visibility in the UI.</summary>
        public bool Subscribed { get; set; }

        /// <summary>Total message count. Null when not selectable.</summary>
        public int? Total { get; set; }

        /// <summary>Unread message count. Null when not selectable.</summary>
        public int? Unread { get; set; }

        /// <summary>
        /// Folder UID validity. When it changes, every UID the client cached for this folder
        /// is stale and must be discarded, or the client will show the wrong messages.
        /// </summary>
        public uint UidValidity { get; set; }

        /// <summary>
        /// Role derived from the folder's SPECIAL-USE flags alone, before uniqueness. Internal
        /// plumbing for the resolution chain — never serialised to the client, which only sees
        /// the final SpecialUse.
        /// </summary>
        [JsonIgnore]
        public string? AttributeRole { get; set; }

        /// <summary>RFC 8474 MAILBOXID when the server supports OBJECTID. Internal plumbing.</summary>
        [JsonIgnore]
        public string? MailboxId { get; set; }

        public List<MailFolderNode> Children { get; set; } = new();
    }
}
