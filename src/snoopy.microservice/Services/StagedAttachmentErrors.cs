namespace weesky.Snoopy.Microservice.Services;

/// <summary>Stable error codes for staged (outgoing) attachments.</summary>
public static class StagedAttachmentErrors
{
    /// <summary>The upload, or the account's running staged total, exceeds
    /// <see cref="Models.Mail.MailOptions.MaxMessageSizeMb"/>. Mapped to 400.</summary>
    public const string TooLarge = "attachment_too_large";
}
