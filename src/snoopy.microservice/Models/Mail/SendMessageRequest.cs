namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// A composed message. 2c2 will add threading (inReplyTo/references) and an identity
/// choice — absent today, no dead fields in waiting.
/// </summary>
public sealed record SendMessageRequest
{
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}
