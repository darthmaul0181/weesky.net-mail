using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The send pipeline. Order is load-bearing: the message is built — and with it the From validated
/// and the staged ids resolved — before anything reaches the wire, SMTP failure keeps the staged
/// files for a retry, and once SMTP accepted, nothing after it may fail the operation — the mail is gone.
/// </summary>
internal sealed class MailSender(
    IOutgoingMessageFactory factory,
    IStagedAttachmentStore staged,
    ISmtpConnectionFactory smtpFactory,
    IRoleFolderLocator locator,
    IMailMessageRepository messages,
    ILogger<MailSender> logger) : IMailSender
{
    public async Task<Result<SendMessageResult>> SendAsync(
        User user, MailAccountConnection connection, SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var stagedScope = connection.StagedScope(user);

        var built = await factory.CreateAsync(user, connection, request, cancellationToken);
        if (built.IsFailure) return Result.Failure<SendMessageResult>(built.Error);

        var sent = await SendBuiltAsync(user, connection, built.Value, cancellationToken);
        if (sent.IsFailure) return sent;

        foreach (var id in request.AttachmentIds) staged.Delete(stagedScope, id);

        return sent;
    }

    public async Task<Result<SendMessageResult>> SendBuiltAsync(
        User user, MailAccountConnection connection, MimeMessage message, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var smtp = await smtpFactory.OpenAsync(connection, cancellationToken);
        if (smtp.IsFailure) return Result.Failure<SendMessageResult>(smtp.Error);
        await using (var session = smtp.Value)
        {
            var sent = await session.SendAsync(message, cancellationToken);
            if (sent.IsFailure) return Result.Failure<SendMessageResult>(sent.Error);
        }

        var appended = await AppendToSentAsync(user, connection, message, cancellationToken);

        return Result.Success(new SendMessageResult(appended));
    }

    /// <summary>Best-effort by design: the mail is already gone, so every failure degrades to false.</summary>
    private async Task<bool> AppendToSentAsync(
        User user, MailAccountConnection connection, MimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await locator.FindAsync(user, connection, "sent", cancellationToken);
            if (sent is null) { logger.LogWarning("No Sent copy: no folder holds the sent role"); return false; }

            var appended = await messages.AppendAsync(user, connection, sent, message, seen: true, cancellationToken);
            if (appended.IsFailure) logger.LogWarning("No Sent copy: {Error}", appended.Error);
            return appended.IsSuccess;
        }
        catch (Exception ex)
        {
            // The mail is already sent; a raw throw here (e.g. preferences DB down) must never
            // fail the request, or the user resends and duplicates it.
            logger.LogError(ex, "No Sent copy: filing the sent message threw for {UserId}", user.WebmailUid);
            return false;
        }
    }
}
