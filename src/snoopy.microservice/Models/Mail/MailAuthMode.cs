namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>How a mailbox is authenticated. Password is first so that it is <c>default</c>.</summary>
public enum MailAuthMode
{
    Password,
    OAuth2
}
