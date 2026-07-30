namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>Everything the composer needs to resume a draft: envelope, editable body, re-staged parts.</summary>
public sealed record OpenedDraft(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string? FromAddress,
    string HtmlBody,
    IReadOnlyList<StagedAttachmentInfo> Attachments,
    string? InReplyTo,
    IReadOnlyList<string> References,
    // Read back off the saved message. Without it a saved High silently resumes as Normal.
    MailPriority Priority);
