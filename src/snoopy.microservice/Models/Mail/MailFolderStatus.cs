namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>Identity snapshot of one live folder, read for override bookkeeping.</summary>
    public class MailFolderStatus
    {
        public string Path { get; set; } = string.Empty;

        public uint UidValidity { get; set; }

        /// <summary>RFC 8474 MAILBOXID when the server supports OBJECTID; null otherwise.</summary>
        public string? MailboxId { get; set; }

        public bool Selectable { get; set; } = true;
    }
}
