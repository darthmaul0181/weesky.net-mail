namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// A composed message. Threading (inReplyTo/references) comes with 2c2b — absent today,
/// no dead fields in waiting.
/// </summary>
public sealed record SendMessageRequest
{
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;

    /// <summary>Identity to send as. Null/empty means the primary address — the 2c1 behaviour.</summary>
    public string? FromAddress { get; init; }

    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}
