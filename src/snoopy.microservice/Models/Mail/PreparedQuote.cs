namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The quotable body (outbound-sanitised, cid images rewritten to staged URLs) and the staged parts.</summary>
public sealed record PreparedQuote(string QuotableHtml, IReadOnlyList<StagedAttachmentInfo> Attachments);
