namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// A decoded attachment, ready to stream to the client. The caller owns <see cref="Content"/> and
/// must dispose it — <c>FileStreamResult</c> does exactly that once the response is written.
/// </summary>
public sealed class MailAttachmentContent
{
    /// <summary>
    /// The decoded bytes, seekable so the response still carries a Content-Length and the browser
    /// can show download progress. A stream rather than a <c>byte[]</c>: the array forced a second
    /// full copy of a part that can reach the configured message size, on the large object heap,
    /// for every concurrent download.
    /// </summary>
    public Stream Content { get; set; } = Stream.Null;

    public string FileName { get; set; } = "attachment";
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>The part's <c>charset</c> parameter, null when it names none. A caller reading the
    /// bytes as text needs it: the bare MIME type above does not carry it, and guessing UTF-8 turns
    /// an <c>iso-8859-1</c> invitation into mojibake.</summary>
    public string? Charset { get; set; }
}
