using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>The calendar part re-read from IMAP, never taken from the client (décision 4), under
/// the size every stored file is held to, and decoded as the card decoded it.</summary>
internal sealed class InvitationPartLoader(IMailMessageRepository messages)
{
    internal const string TooLarge = "invitation_too_large";

    internal async Task<Result<string, ResponderFailure>> LoadAsync(
        User user, MailAccountConnection connection, string folder, uint uid, string part, CancellationToken cancellationToken)
    {
        var attachment = await messages.GetAttachmentAsync(user, connection, folder, uid, part, cancellationToken);
        if (attachment.IsFailure)
            return Result.Failure<string, ResponderFailure>(new ResponderFailure(
                attachment.Error is ImapSession.FolderNotFound or ImapSession.MessageNotFound or ImapSession.AttachmentNotFound ? 404 : 502,
                attachment.Error));
        using var content = attachment.Value.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > IcsGuards.MaxIcsBytes)
            return Result.Failure<string, ResponderFailure>(new ResponderFailure(422, TooLarge));
        // The card's decode, not a second one: a BOM'd or iso-8859-1 part must read here exactly as
        // it read when the invitation was shown, or the answer applies to a different file.
        return MailMessageMapper.DecodeText(buffer.GetBuffer(), (int)buffer.Length, attachment.Value.Charset);
    }
}
