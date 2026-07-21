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
    public DateTimeOffset Date { get; set; }

    /// <summary>SPF/DKIM verdicts from the receiving server. Null when the message carries no Authentication-Results.</summary>
    public MailAuthentication? Authentication { get; set; }

    /// <summary>The spam filter's verdict. Null when the message carries no recognised anti-spam header.</summary>
    public MailSpamScore? SpamScore { get; set; }

    /// <summary>Sanitised HTML body. Empty when the message is text-only.</summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>Plain-text body. Empty when the message is HTML-only.</summary>
    public string TextBody { get; set; } = string.Empty;

    /// <summary>Remote images withheld by the sanitiser, for the "show images" prompt.</summary>
    public int BlockedImageCount { get; set; }

    public List<MailAttachmentInfo> Attachments { get; set; } = new();
}
