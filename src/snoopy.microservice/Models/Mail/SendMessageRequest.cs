using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// A composed message.
///
/// Every dimension is bounded, as every other request on this API is: one authenticated caller
/// must not be able to turn a single request into a mass mailing, nor into a body no mail server
/// was ever going to accept. The recipient ceiling is the one rule an attribute cannot carry —
/// it counts the three lists together — so <c>MailComposeController.NormalizeOutgoing</c> holds it.
/// </summary>
public record SendMessageRequest
{
    /// <summary>
    /// To + Cc + Bcc together. A real message reaches a handful of people; a hundred is already
    /// a mailing list's job, and Postfix's own recipient limit sits not far above.
    /// </summary>
    public const int MaxRecipients = 100;

    /// <summary>RFC 5322's maximum unfolded line, which is what a Subject becomes on the wire.</summary>
    public const int MaxSubjectLength = 998;

    /// <summary>
    /// Same ceiling the inbound sanitiser applies to a body it renders
    /// (<c>MailHtmlSanitizer.MaxInputLength</c>): a composer that cannot produce more than this is
    /// not asked to accept more than this. Inline images do not count against it — they are staged
    /// attachments referenced by cid, never base64 inside the body.
    /// </summary>
    public const int MaxBodyLength = 2 * 1024 * 1024;

    /// <summary>The staging store already caps an account at 50 live entries; this refuses a list
    /// naming more before any of them is resolved.</summary>
    public const int MaxAttachments = 50;

    /// <summary>A long thread legitimately accumulates these, but not without end.</summary>
    public const int MaxReferences = 100;

    [MaxLength(MaxRecipients, ErrorMessage = "A message cannot name more than 100 recipients")]
    public IReadOnlyList<string> To { get; init; } = [];

    [MaxLength(MaxRecipients, ErrorMessage = "A message cannot name more than 100 recipients")]
    public IReadOnlyList<string> Cc { get; init; } = [];

    [MaxLength(MaxRecipients, ErrorMessage = "A message cannot name more than 100 recipients")]
    public IReadOnlyList<string> Bcc { get; init; } = [];

    [StringLength(MaxSubjectLength, ErrorMessage = "The subject must be at most 998 characters")]
    public string Subject { get; init; } = string.Empty;

    [StringLength(MaxBodyLength, ErrorMessage = "The message body is too large")]
    public string HtmlBody { get; init; } = string.Empty;

    /// <summary>
    /// Set to send the message as text/plain and nothing else — no HTML twin, no inline parts.
    /// Non-null is the format itself, so a flag can never contradict the body it describes; null
    /// is an HTML message, which is every message composed before this field existed.
    /// </summary>
    [StringLength(MaxBodyLength, ErrorMessage = "The message body is too large")]
    public string? TextBody { get; init; }

    /// <summary>Identity to send as. Null/empty means the primary address — the 2c1 behaviour.</summary>
    [StringLength(320, ErrorMessage = "An address must be at most 320 characters")]
    public string? FromAddress { get; init; }

    [MaxLength(MaxAttachments, ErrorMessage = "A message cannot carry more than 50 attachments")]
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];

    /// <summary>Message-Id being replied to, bare (no angle brackets). Absent on a fresh message.</summary>
    [StringLength(MaxSubjectLength, ErrorMessage = "The In-Reply-To id is too long")]
    public string? InReplyTo { get; init; }

    /// <summary>References chain for the reply, oldest first, bare ids. Empty on a fresh message.</summary>
    [MaxLength(MaxReferences, ErrorMessage = "The references chain is too long")]
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>Priority to declare. Normal writes no header at all — see MailPriorityHeaders.</summary>
    public MailPriority Priority { get; init; } = MailPriority.Normal;
}
