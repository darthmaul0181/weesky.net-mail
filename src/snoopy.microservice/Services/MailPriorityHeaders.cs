using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Declares a priority on an outgoing message. Three headers, because no single one is read by
/// everybody: Outlook and Exchange read Importance, Thunderbird and Roundcube read X-Priority,
/// older Microsoft clients read X-MSMail-Priority. Written raw rather than through MimeKit's
/// MimeMessage.Importance / .XPriority properties: those cannot express the "1 (Highest)" spelling
/// the wire actually carries, and there is no property at all for X-MSMail-Priority — three
/// headers written three different ways would be the drift this pair exists to prevent.
/// </summary>
internal static class MailPriorityHeaders
{
    public static void Apply(MimeMessage message, MailPriority priority)
    {
        if (priority == MailPriority.Normal) return;

        var high = priority == MailPriority.High;
        message.Headers.Add("X-Priority", high ? "1 (Highest)" : "5 (Lowest)");
        message.Headers.Add("Importance", high ? "high" : "low");
        message.Headers.Add("X-MSMail-Priority", high ? "High" : "Low");
    }
}
