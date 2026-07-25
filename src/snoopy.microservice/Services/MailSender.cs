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
    IMailFolderRepository folders,
    IFolderRoleStore roles,
    IMailMessageRepository messages,
    ILogger<MailSender> logger) : IMailSender
{
    public async Task<Result<SendMessageResult>> SendAsync(
        User user, string password, SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var userId = user.WebmailUid;

        var built = await factory.CreateAsync(user, request, cancellationToken);
        if (built.IsFailure) return Result.Failure<SendMessageResult>(built.Error);
        var message = built.Value;

        var smtp = await smtpFactory.OpenAsync(user.Email, password, cancellationToken);
        if (smtp.IsFailure) return Result.Failure<SendMessageResult>(smtp.Error);
        await using (var session = smtp.Value)
        {
            var sent = await session.SendAsync(message, cancellationToken);
            if (sent.IsFailure) return Result.Failure<SendMessageResult>(sent.Error);
        }

        var appended = await AppendToSentAsync(user, password, userId, message, cancellationToken);

        foreach (var id in request.AttachmentIds) staged.Delete(userId.ToString(), id);

        return Result.Success(new SendMessageResult(appended));
    }

    /// <summary>Best-effort by design: the mail is already gone, so every failure degrades to false.</summary>
    private async Task<bool> AppendToSentAsync(
        User user, string password, Guid userId, MimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var tree = await folders.GetTreeAsync(user, password, cancellationToken);
            if (tree.IsFailure) { logger.LogWarning("No Sent copy: folder tree unavailable"); return false; }

            var overrides = await roles.GetAsync(userId, cancellationToken);
            var sent = FolderRoleResolver.Resolve(tree.Value, overrides).Roles
                .FirstOrDefault(r => r.Role == "sent" && r.FolderPath != null);
            if (sent == null) { logger.LogWarning("No Sent copy: no folder holds the sent role"); return false; }

            var appended = await messages.AppendAsync(user, password, sent.FolderPath!, message, seen: true, cancellationToken);
            if (appended.IsFailure) logger.LogWarning("No Sent copy: {Error}", appended.Error);
            return appended.IsSuccess;
        }
        catch (Exception ex)
        {
            // The mail is already sent; a raw throw here (e.g. preferences DB down) must never
            // fail the request, or the user resends and duplicates it.
            logger.LogError(ex, "No Sent copy: filing the sent message threw for {UserId}", userId);
            return false;
        }
    }
}
