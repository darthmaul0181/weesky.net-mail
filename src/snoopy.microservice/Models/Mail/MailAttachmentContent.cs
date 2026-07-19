namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A decoded attachment, ready to stream to the client.</summary>
public sealed class MailAttachmentContent
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "attachment";
    public string ContentType { get; set; } = "application/octet-stream";
}
