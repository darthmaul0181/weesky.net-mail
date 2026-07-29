using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

public interface IMailSender
{
    /// <summary>Returned as the failure when a staged attachment id no longer resolves.</summary>
    const string UnknownAttachment = IOutgoingMessageFactory.UnknownAttachment;

    /// <summary>Returned when the requested From is neither the primary address nor a live alias.</summary>
    const string ForbiddenFrom = IOutgoingMessageFactory.ForbiddenFrom;

    /// <summary>Builds, submits over SMTP, files the Sent copy and purges the staged files.</summary>
    Task<Result<SendMessageResult>> SendAsync(User user, MailAccountConnection connection, SendMessageRequest request, CancellationToken cancellationToken);
}
