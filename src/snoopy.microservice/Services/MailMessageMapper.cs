using MailKit;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Maps what MailKit fetched — summaries, envelopes, body parts — onto the models the API
/// serves. Pure transcription: nothing here talks to a server.
/// </summary>
internal static class MailMessageMapper
{
    /// <summary>One mapping for list rows and search hits — the fields cannot drift apart.</summary>
    internal static T FillSummary<T>(T summary, IMessageSummary item) where T : MailMessageSummary
    {
        var sender = item.Envelope?.From?.Mailboxes?.FirstOrDefault();

        summary.Uid = item.UniqueId.Id;
        summary.Subject = item.Envelope?.Subject ?? string.Empty;
        summary.FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty;
        summary.FromAddress = sender?.Address ?? string.Empty;
        summary.To = ToAddressInfos(item.Envelope?.To);
        // Arrival date, not the Date header. The page window is a range of sequence
        // numbers, so the list is ordered by arrival; showing the header date would
        // print a date that contradicts the row's own position — a message written in
        // May but delivered in June sits among the June messages, and saying "May"
        // there reads as a sorting bug. The header date is still shown in the reader,
        // where it answers a different question: when the sender wrote it.
        summary.Date = item.InternalDate ?? item.Envelope?.Date ?? DateTimeOffset.MinValue;
        summary.Seen = item.Flags?.HasFlag(MessageFlags.Seen) ?? false;
        summary.Flagged = item.Flags?.HasFlag(MessageFlags.Flagged) ?? false;
        summary.Answered = item.Flags?.HasFlag(MessageFlags.Answered) ?? false;
        summary.HasAttachments = item.Attachments?.Any() ?? false;
        summary.Size = item.Size ?? 0;
        summary.Preview = item.PreviewText ?? string.Empty;
        summary.Priority = item.Headers is { } headers ? MailPriorityReader.Parse(headers) : MailPriority.Normal;
        return summary;
    }

    public static List<MailAddressInfo> ToAddressInfos(InternetAddressList? addresses) =>
        addresses?.Mailboxes?.Select(m => new MailAddressInfo(m.Name ?? string.Empty, m.Address)).ToList() ?? [];

    /// <summary>Threading and reply-routing headers — 2c2b's transcription duty on the detail.</summary>
    internal static void ApplyThreading(MailMessageDetail detail, MimeMessage message)
    {
        detail.MessageId = string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId;
        detail.References = message.References?.ToList() ?? [];
        detail.InReplyTo = string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo;
        detail.ReplyTo = ToAddressInfos(message.ReplyTo);
        detail.Bcc = ToAddressInfos(message.Bcc);
    }

    // Servers report Content-ID with or without <>; the HTML's cid: references are always bare.
    internal static string? TrimAngleBrackets(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return null;
        var trimmed = contentId.Trim();
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>')) trimmed = trimmed[1..^1];
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Whether a body part belongs on the message's part list. Being an attachment or carrying a
    /// file name is not enough to ask for: a logo embedded as <c>Content-Disposition: inline</c>
    /// with no file name — how Vaultwarden and others ship theirs — is neither, and dropping it
    /// left the reader with a <c>cid:</c> it had nothing to resolve against. A Content-ID is
    /// exactly the marker that the body means to display the part, so it earns a place too. What
    /// remains excluded is the message's own text and html, which carry none of the three.
    /// </summary>
    internal static bool IsListedPart(BodyPartBasic part) =>
        part.IsAttachment
        || !string.IsNullOrEmpty(part.FileName)
        || !string.IsNullOrEmpty(TrimAngleBrackets(part.ContentId));
}
