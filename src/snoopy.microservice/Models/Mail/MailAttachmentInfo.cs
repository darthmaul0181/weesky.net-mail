namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>An attachment as listed on a message, without its content.</summary>
    public class MailAttachmentInfo
    {
        /// <summary>
        /// MIME part specifier — the download handle, opaque to the client. Addressed this
        /// way rather than by position: an index drifts, so a stale client link would fetch
        /// the wrong file instead of failing.
        /// </summary>
        public string Part { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>Size in octets, read from the body structure — no download required.</summary>
        public uint Size { get; set; }

        /// <summary>
        /// True for a part the HTML body references by cid:, not a real attachment. The UI
        /// hides these: they are already visible inside the message.
        /// </summary>
        public bool IsInline { get; set; }
    }
}
