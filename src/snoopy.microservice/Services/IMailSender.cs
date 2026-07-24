using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

public interface IMailSender
{
    /// <summary>Returned as the failure when a staged attachment id no longer resolves.</summary>
    const string UnknownAttachment = "unknown_attachment";

    /// <summary>Builds, submits over SMTP, files the Sent copy and purges the staged files.</summary>
    Task<Result<SendMessageResult>> SendAsync(User user, string password, SendMessageRequest request, CancellationToken cancellationToken);
}
