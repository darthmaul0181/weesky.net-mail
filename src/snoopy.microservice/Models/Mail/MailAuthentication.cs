namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>SPF, DKIM and DMARC verdicts as the receiving server reported them, plus the header they came from.</summary>
public sealed record MailAuthentication(string? Spf, string? Dkim, string? Dmarc, string Raw);
