namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// What the user is searching for. Quick is the fast-bar text (subject OR sender);
/// the rest are the advanced form's fields, combined with AND.
/// </summary>
public sealed record MailSearchCriteria(
    string? Quick,
    string? From,
    string? To,
    string? Subject,
    string? Text,
    int? SinceDays,
    bool Unread,
    bool Flagged,
    bool HasAttachment);
