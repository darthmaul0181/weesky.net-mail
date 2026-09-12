using CSharpFunctionalExtensions;
using MimeKit;
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

    /// <summary>Sends a message something else composed — an invitation reply — through the account's
    /// session and files it in Sent like any send. The From is the caller's responsibility.</summary>
    Task<Result<SendMessageResult>> SendBuiltAsync(User user, MailAccountConnection connection, MimeMessage message, CancellationToken cancellationToken);
}
