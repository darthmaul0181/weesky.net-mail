namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A single message, ready to render.</summary>
public sealed class MailMessageDetail
{
    public uint Uid { get; set; }
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Folder UID validity — see MailFolderPage for why the client needs it.</summary>
    public uint UidValidity { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public List<MailAddressInfo> To { get; set; } = [];
    public List<MailAddressInfo> Cc { get; set; } = [];

    /// <summary>RFC 5322 Message-Id, bare (no angle brackets). Null when the message carries none.</summary>
    public string? MessageId { get; set; }

    /// <summary>References chain, oldest first, bare ids. Empty when absent.</summary>
    public List<string> References { get; set; } = [];

    /// <summary>In-Reply-To, bare id. Null when absent.</summary>
    public string? InReplyTo { get; set; }

    /// <summary>Reply-To mailboxes — the reply target when present. Empty when absent.</summary>
    public List<MailAddressInfo> ReplyTo { get; set; } = [];

    /// <summary>Bcc mailboxes — kept on a Sent copy; empty on received mail. Feeds Edit-as-new.</summary>
    public List<MailAddressInfo> Bcc { get; set; } = [];

    public DateTimeOffset Date { get; set; }

    /// <summary>SPF/DKIM verdicts from the receiving server. Null when the message carries no Authentication-Results.</summary>
    public MailAuthentication? Authentication { get; set; }

    /// <summary>The spam filter's verdict. Null when the message carries no recognised anti-spam header.</summary>
    public MailSpamScore? SpamScore { get; set; }

    /// <summary>Expanded-header details (List-Id, envelope domain, DKIM domain, unsubscribe link, TLS). Each null when absent.</summary>
    public string? MailingList { get; set; }

    public string? SentBy { get; set; }
    public string? SignedBy { get; set; }
    public string? UnsubscribeUrl { get; set; }
    public bool? TlsReceived { get; set; }

    /// <summary>Priority the sender declared. Normal when the message carries no priority header.</summary>
    public MailPriority Priority { get; set; } = MailPriority.Normal;

    /// <summary>Sanitised HTML body. Empty when the message is text-only.</summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>Plain-text body. Empty when the message is HTML-only.</summary>
    public string TextBody { get; set; } = string.Empty;

    /// <summary>Remote images withheld by the sanitiser, for the "show images" prompt.</summary>
    public int BlockedImageCount { get; set; }

    /// <summary>
    /// True when the HTML body exceeded the sanitiser's input ceiling and only its leading part was
    /// kept. Without it the reader sees a message that simply stops, with nothing saying why.
    /// </summary>
    public bool Truncated { get; set; }

    public List<MailAttachmentInfo> Attachments { get; set; } = new();
}
