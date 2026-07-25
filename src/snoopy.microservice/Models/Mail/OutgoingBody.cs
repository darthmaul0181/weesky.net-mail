namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A composed body ready to send: sanitised HTML and its plain-text alternative.</summary>
public sealed record OutgoingBody(string Html, string Text);
