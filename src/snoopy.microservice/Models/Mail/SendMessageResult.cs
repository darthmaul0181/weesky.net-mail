namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The mail is gone either way; false only means the Sent copy could not be filed.</summary>
public sealed record SendMessageResult(bool AppendedToSent);
