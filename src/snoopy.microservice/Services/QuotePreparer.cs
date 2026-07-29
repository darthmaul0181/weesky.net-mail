using System.Net;
using AngleSharp;
using AngleSharp.Html.Parser;
using CSharpFunctionalExtensions;
using Ganss.Xss;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// One pass over the original: the raw body goes through the OUTGOING policy (it is about to be
/// sent again, not displayed), then every cid the body references is staged as an inline part and
/// its src rewritten to the staged-content URL. The invariant is a quotableHtml with no cid: left.
/// Forward and EditAsNew re-stage the real attachments too — server-side, never via the browser.
/// </summary>
internal sealed class QuotePreparer(IOutgoingMailSanitizer sanitizer, IStagedAttachmentStore staged) : IQuotePreparer
{
    private readonly HtmlParser _parser = new();

    public async Task<Result<PreparedQuote>> PrepareAsync(
        string stagedScope, MimeMessage message, QuotePurpose purpose, CancellationToken cancellationToken)
    {
        List<StagedAttachmentInfo> attachments = [];
        // MimeMessage.BodyParts and .Attachments overlap: an Outlook inline image carries an
        // attachment disposition too, and would otherwise be staged twice on a forward.
        var inlined = new HashSet<MimeEntity>(ReferenceEqualityComparer.Instance);
        string quotable;

        var raw = message.HtmlBody;
        if (string.IsNullOrEmpty(raw))
        {
            // A text-only original is quoted from its TextBody — the composer knows one input format.
            quotable = TextToHtml(message.TextBody ?? string.Empty);
        }
        else
        {
            var sanitized = sanitizer.Prepare(raw).Html;
            var document = _parser.ParseDocument($"<body>{sanitized}</body>");
            var stagedByCid = new Dictionary<string, StagedAttachmentInfo>(StringComparer.Ordinal);

            foreach (var img in document.Body!.QuerySelectorAll("img").ToList())
            {
                var src = img.GetAttribute("src") ?? string.Empty;
                if (!src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) continue;

                var contentId = src[4..];
                if (!stagedByCid.TryGetValue(contentId, out var info))
                {
                    var part = FindImagePart(message, contentId);
                    if (part == null) { img.Remove(); continue; } // dangling or non-image cid
                    var inline = await StagePartAsync(stagedScope, part, contentId, cancellationToken);
                    if (inline.IsFailure) return Result.Failure<PreparedQuote>(inline.Error);
                    info = inline.Value;
                    stagedByCid[contentId] = info;
                    inlined.Add(part);
                    attachments.Add(info);
                }

                img.SetAttribute("src", StagedContentUrl.For(info.Id));
            }

            // Same re-serialisation as the sanitizer: Ganss's formatter keeps attribute escaping.
            quotable = document.Body.ChildNodes.ToHtml(HtmlFormatter.Instance);
        }

        if (purpose is QuotePurpose.Forward or QuotePurpose.EditAsNew)
        {
            foreach (var entity in message.Attachments)
            {
                if (inlined.Contains(entity)) continue;

                var attachment = await StageAttachmentAsync(stagedScope, entity, cancellationToken);
                if (attachment.IsFailure) return Result.Failure<PreparedQuote>(attachment.Error);
                attachments.Add(attachment.Value);
            }
        }

        // On a mid-way failure above, already-staged files linger — the TTL sweep reclaims them.
        return Result.Success(new PreparedQuote(quotable, attachments));
    }

    private static MimePart? FindImagePart(MimeMessage message, string contentId) =>
        message.BodyParts.OfType<MimePart>().FirstOrDefault(p =>
            string.Equals(p.ContentId, contentId, StringComparison.Ordinal)
            && p.ContentType.IsMimeType("image", "*"));

    private async Task<Result<StagedAttachmentInfo>> StagePartAsync(
        string stagedScope, MimePart part, string? contentId, CancellationToken cancellationToken)
    {
        // Content.Open() decodes on the fly — no in-memory buffering of a possibly large part.
        if (part.Content == null)
            return Result.Failure<StagedAttachmentInfo>("An inline part of this message is unreadable");

        await using var content = part.Content.Open();
        return await staged.SaveAsync(
            stagedScope, part.FileName ?? "inline", part.ContentType.MimeType, content, cancellationToken, contentId);
    }

    private async Task<Result<StagedAttachmentInfo>> StageAttachmentAsync(
        string stagedScope, MimeEntity entity, CancellationToken cancellationToken)
    {
        if (entity is MimePart part) return await StagePartAsync(stagedScope, part, null, cancellationToken);

        // An attached message has no decodable content; the INNER message alone is the .eml. Writing
        // the entity would prepend the part's own message/rfc822 headers, so the staged file would
        // not parse standalone and the sender would wrap it a second time.
        await using var buffer = new MemoryStream();
        if (entity is MessagePart { Message: { } inner }) await inner.WriteToAsync(buffer, cancellationToken);
        else await entity.WriteToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var name = entity.ContentDisposition?.FileName ?? "attached-message.eml";
        return await staged.SaveAsync(stagedScope, name, entity.ContentType.MimeType, buffer, cancellationToken);
    }

    /// <summary>Escaped text with its line structure rendered — the text-only quoting path.</summary>
    internal static string TextToHtml(string text)
    {
        var escaped = WebUtility.HtmlEncode(text);
        return $"<div>{escaped.Replace("\r\n", "\n").Replace("\n", "<br>")}</div>";
    }
}
