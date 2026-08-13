namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One conversation of a grouped page: its messages, newest first.</summary>
public sealed class MailThread
{
    public List<MailMessageSummary> Messages { get; set; } = [];
}
