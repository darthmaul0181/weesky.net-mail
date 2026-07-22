namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A message flag the client may set or clear. Serialised as a string in JSON.</summary>
public enum MailFlag
{
    Seen,
    Flagged
}
