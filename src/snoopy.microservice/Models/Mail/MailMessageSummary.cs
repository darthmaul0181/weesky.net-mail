namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>One row of the message list. Envelope-level only — no body is fetched.</summary>
    public class MailMessageSummary
    {
        /// <summary>IMAP UID. Valid only for the UidValidity of its page.</summary>
        public uint Uid { get; set; }

        public string Subject { get; set; } = string.Empty;

        /// <summary>Display name of the first sender, falling back to the address.</summary>
        public string FromName { get; set; } = string.Empty;

        public string FromAddress { get; set; } = string.Empty;

        /// <summary>Date the message claims, falling back to the server's internal date.</summary>
        public DateTimeOffset Date { get; set; }

        public bool Seen { get; set; }
        public bool Flagged { get; set; }
        public bool Answered { get; set; }
        public bool HasAttachments { get; set; }

        /// <summary>Size in octets.</summary>
        public uint Size { get; set; }

        /// <summary>Short body extract for the list row. Empty when the server cannot supply one.</summary>
        public string Preview { get; set; } = string.Empty;
    }
}
