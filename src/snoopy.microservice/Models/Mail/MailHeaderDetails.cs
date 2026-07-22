namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>Delivery details for the reader's expanded header. Any field is null when its header is absent.</summary>
public sealed record MailHeaderDetails(
    string? MailingList,
    string? SentBy,
    string? SignedBy,
    string? UnsubscribeUrl,
    bool? TlsReceived);
