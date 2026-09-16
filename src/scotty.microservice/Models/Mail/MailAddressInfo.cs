namespace weesky.Scotty.Microservice.Models.Mail;

/// <summary>One recipient. Name is empty when the message carried no display name.</summary>
public sealed record MailAddressInfo(string Name, string Address);
