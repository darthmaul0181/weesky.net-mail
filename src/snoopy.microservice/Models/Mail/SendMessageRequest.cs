namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A composed message.</summary>
public record SendMessageRequest
{
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;

    /// <summary>
    /// Set to send the message as text/plain and nothing else — no HTML twin, no inline parts.
    /// Non-null is the format itself, so a flag can never contradict the body it describes; null
    /// is an HTML message, which is every message composed before this field existed.
    /// </summary>
    public string? TextBody { get; init; }

    /// <summary>Identity to send as. Null/empty means the primary address — the 2c1 behaviour.</summary>
    public string? FromAddress { get; init; }

    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];

    /// <summary>Message-Id being replied to, bare (no angle brackets). Absent on a fresh message.</summary>
    public string? InReplyTo { get; init; }

    /// <summary>References chain for the reply, oldest first, bare ids. Empty on a fresh message.</summary>
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>Priority to declare. Normal writes no header at all — see MailPriorityHeaders.</summary>
    public MailPriority Priority { get; init; } = MailPriority.Normal;
}
