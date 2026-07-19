using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Makes a message body safe to render. A message body is hostile input by construction.
    ///
    /// The allowed tag and style set is a contract shared with the rich editor of the
    /// composing slice: replying round-trips a sanitised body through the editor and back out
    /// to SMTP, so formatting degrades on every pass if the two ends disagree.
    ///
    /// This is the first of two independent barriers. The second is the sandboxed iframe the
    /// client renders into, which can neither run scripts nor reach our origin.
    /// </summary>
    public interface IMailHtmlSanitizer
    {
        SanitizedHtml Sanitize(string html);
    }
}
