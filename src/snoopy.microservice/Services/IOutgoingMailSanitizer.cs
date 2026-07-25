using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Sanitises composed HTML for sending. A policy of its own, deliberately not
/// IMailHtmlSanitizer: that one blocks remote images and culls url() — display rules,
/// absurd on the way out.
/// </summary>
public interface IOutgoingMailSanitizer
{
    OutgoingBody Prepare(string html);
}
