namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>SPF and DKIM verdicts as the receiving server reported them, plus the header they came from.</summary>
public sealed record MailAuthentication(string? Spf, string? Dkim, string Raw);
