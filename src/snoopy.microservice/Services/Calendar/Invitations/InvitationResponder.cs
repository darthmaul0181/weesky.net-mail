using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

public interface IInvitationResponder
{
    /// <summary>Records the user's answer in the calendar, mails it to the organizer, and trashes a
    /// declined invitation's message. The failure is the one the endpoint answers with.</summary>
    Task<Result<InvitationResponse, ResponderFailure>> RespondAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Décision 4, in order: the part is re-read from IMAP, judged, matched against the base; the
/// calendar is written through the CalDAV PUT path under the stored name when the event exists;
/// then the REPLY goes out, and a decline's mail goes to the trash once the reply has left. A
/// failure after the write never undoes it: the answer says what was and was not done.
/// </summary>
internal sealed class InvitationResponder(
    IMailMessageRepository messages,
    InvitationReader reader,
    ICalendarStore calendars,
    IDavCalendarWriter writer,
    IMailSender sender,
    IRoleFolderLocator locator,
    IProfileReader profiles,
    ILogger<InvitationResponder> logger) : IInvitationResponder
{
    // Stable codes, not prose: they travel as the envelope's message and are what the client
    // branches on to say the refusal in the reader's own language.
    internal const string NoOrganizer = "The invitation names no organizer to answer";
    internal const string NotAddressed = "invitation_not_addressed";
    internal const string NotInCalendar = "invitation_not_in_calendar";
    internal const string OccurrenceOnly = "invitation_occurrence_only";
    internal const string Stale = "invitation_stale";
    internal const string Incompatible = "invitation_incompatible_answer";
    internal const string NoCalendar = "invitation_no_calendar";
    internal const string Unreadable = "invitation_unreadable";
    internal const string TooLarge = "invitation_too_large";
    internal const string UnknownAnswer = "invitation_unknown_answer";
    internal const string CalendarConflict = "calendar_conflict";
    internal const string CalendarBusy = "calendar_busy";
    internal const string CalendarRefused = "calendar_refused";

    private static readonly Result<bool, ResponderFailure> Done = Result.Success<bool, ResponderFailure>(true);

    public async Task<Result<InvitationResponse, ResponderFailure>> RespondAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var ics = await ReadPartAsync(user, connection, request, cancellationToken);
        if (ics.IsFailure) return Result.Failure<InvitationResponse, ResponderFailure>(ics.Error);

        var reading = InvitationParser.Read(ics.Value);
        if (reading.Invitation is not { } parsed)
        {
            logger.LogWarning("The calendar part of {Uid} was refused: {Reason}", request.Uid, reading.Reason);
            return Refused(422, Unreadable);
        }
        if (parsed.OccurrenceOnly) return Refused(400, OccurrenceOnly);
        var fits = parsed.Method switch
        {
            InvitationMethod.Request => request.Answer is not InvitationAnswer.Remove,
            _ => request.Answer is InvitationAnswer.Remove,
        };
        if (!fits) return Refused(400, Incompatible);

        var context = await reader.ResolveAsync(user, connection, parsed, cancellationToken);
        if (context.Presence is InvitationPresence.Newer) return Refused(409, Stale);
        var answering = request.Answer is InvitationAnswer.Accepted or InvitationAnswer.Tentative or InvitationAnswer.Declined;
        if (answering && context.AddressedTo is null) return Refused(400, NotAddressed);

        var written = await WriteAsync(user, request, ics.Value, context, cancellationToken);
        if (written.IsFailure) return Result.Failure<InvitationResponse, ResponderFailure>(written.Error);

        var (replySent, replyError) = answering
            ? await SendReplyAsync(user, connection, request, parsed, context.AddressedTo!, cancellationToken)
            : (false, null);

        var trashed = request.Answer is InvitationAnswer.Declined && replySent
            && await TrashAsync(user, connection, request, cancellationToken);

        var after = await reader.ResolveAsync(user, connection, parsed, cancellationToken);
        return new InvitationResponse(InvitationReader.Block(parsed, after, request.Part), replySent, replyError, trashed);
    }

    private async Task<Result<string, ResponderFailure>> ReadPartAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var part = await messages.GetAttachmentAsync(user, connection, request.Folder, request.Uid, request.Part, cancellationToken);
        if (part.IsFailure)
            return Result.Failure<string, ResponderFailure>(new ResponderFailure(
                part.Error is ImapSession.FolderNotFound or ImapSession.MessageNotFound or ImapSession.AttachmentNotFound ? 404 : 502,
                part.Error));
        using var content = part.Value.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > IcsGuards.MaxIcsBytes)
            return Result.Failure<string, ResponderFailure>(new ResponderFailure(422, TooLarge));
        // The card's decode, not a second one: a BOM'd or iso-8859-1 part must read here exactly as
        // it read when the invitation was shown, or the buttons answer a different file.
        return MailMessageMapper.DecodeText(buffer.GetBuffer(), (int)buffer.Length, part.Value.Charset);
    }

    private async Task<Result<bool, ResponderFailure>> WriteAsync(
        User user, RespondInvitationRequest request, string ics, InvitationContext context, CancellationToken cancellationToken)
    {
        var stored = context.Stored;
        switch (request.Answer)
        {
            case InvitationAnswer.Declined:
            case InvitationAnswer.Remove:
                if (stored is null)
                    return request.Answer is InvitationAnswer.Remove ? Failed(400, NotInCalendar) : Done;
                return Map(await writer.DeleteAsync(user.WebmailUid, stored.CalendarId, stored.DavName, cancellationToken));

            case InvitationAnswer.AddOnly:
                return await PutAsync(user, request, stored, PartStatRewriter.StripMethod(SourceOf(context, ics)), cancellationToken);

            case InvitationAnswer.Accepted:
            case InvitationAnswer.Tentative:
                var partStat = request.Answer is InvitationAnswer.Accepted ? "ACCEPTED" : "TENTATIVE";
                var rewritten = PartStatRewriter.Rewrite(SourceOf(context, ics), context.AddressedTo!, partStat);
                if (rewritten is null) return Failed(400, NotAddressed);
                return await PutAsync(user, request, stored, rewritten, cancellationToken);

            // Named rather than folded into a default: the binder takes any integer, and an
            // undefined one must not inherit the behaviour of whichever case sits last.
            default:
                return Failed(400, UnknownAnswer);
        }
    }

    /// <summary>Which file the calendar is written from: the stored one when the calendar already
    /// holds this very version, so a phone's alarm survives the answer; the organizer's otherwise,
    /// its new version replacing everything (décision 4).</summary>
    private static string SourceOf(InvitationContext context, string ics) =>
        context.Presence is InvitationPresence.Current ? context.Stored!.IcsRaw : ics;

    /// <summary>A rewrite keeps the stored name and calendar — the only way to update what a
    /// phone put under a name of its own (décision 4); a creation takes calendarId, else the default.
    /// The wanted calendar is looked up in the user's own list, so an id naming another user's
    /// collection is simply not found and the default answers instead.</summary>
    private async Task<Result<bool, ResponderFailure>> PutAsync(
        User user, RespondInvitationRequest request, StoredEventRef? stored, string ics, CancellationToken cancellationToken)
    {
        Guid calendarId; string davName;
        if (stored is not null) (calendarId, davName) = (stored.CalendarId, stored.DavName);
        else
        {
            var list = await calendars.ListAsync(user.WebmailUid, cancellationToken);
            var target = request.CalendarId is { } wanted ? list.FirstOrDefault(c => c.Id == wanted) : null;
            target ??= list.FirstOrDefault(c => c.IsDefault) ?? list.FirstOrDefault();
            if (target is null) return Failed(502, NoCalendar);
            (calendarId, davName) = (target.Id, $"{Guid.NewGuid()}.ics");
        }

        return Map(await writer.PutAsync(user.WebmailUid, calendarId, davName, ics, cancellationToken, cause: RevisionCause.Webmail));
    }

    private static Result<bool, ResponderFailure> Map(DavWriteOutcome outcome) => outcome.Status switch
    {
        DavWriteStatus.Created or DavWriteStatus.Replaced or DavWriteStatus.Deleted or DavWriteStatus.NotFound => Done,
        DavWriteStatus.UidConflict or DavWriteStatus.AlreadyExists or DavWriteStatus.PreconditionFailed =>
            Failed(409, CalendarConflict),
        DavWriteStatus.Busy => Failed(502, CalendarBusy),
        _ => Failed(422, CalendarRefused),
    };

    private async Task<(bool Sent, string? Error)> SendReplyAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, ParsedInvitation parsed,
        string addressedTo, CancellationToken cancellationToken)
    {
        if (parsed.Organizer is null) return (false, NoOrganizer);
        var partStat = request.Answer switch
        {
            InvitationAnswer.Accepted => "ACCEPTED", InvitationAnswer.Tentative => "TENTATIVE", _ => "DECLINED",
        };
        var name = await profiles.GetDisplayNameAsync(user, cancellationToken);
        var message = ReplyComposer.Compose(new ReplyInput(
            addressedTo, string.IsNullOrWhiteSpace(name) ? user.FullName : name,
            parsed.Organizer.Email, parsed.Organizer.Name, parsed, partStat,
            request.TimeZone, request.Language, DateTime.UtcNow));
        var sent = await sender.SendBuiltAsync(user, connection, message, cancellationToken);
        if (sent.IsFailure)
            logger.LogWarning("The reply to invitation {Uid} could not be sent: {Error}", parsed.Uid, sent.Error);
        return sent.IsSuccess ? (true, null) : (false, sent.Error);
    }

    /// <summary>Best effort: the reply is gone; a move that fails leaves the mail in place, and the
    /// answer says so (décision 4, risques).</summary>
    private async Task<bool> TrashAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var trash = await locator.FindAsync(user, connection, "trash", cancellationToken);
        if (trash is null || string.Equals(trash, request.Folder, StringComparison.Ordinal)) return false;
        var moved = await messages.MoveOrCopyAsync(user, connection, request.Folder, [request.Uid], trash, copy: false, cancellationToken);
        if (moved.IsFailure) logger.LogWarning("Declined invitation {Uid} stays in {Folder}: {Error}", request.Uid, request.Folder, moved.Error);
        return moved.IsSuccess;
    }

    private static Result<bool, ResponderFailure> Failed(int status, string message) =>
        Result.Failure<bool, ResponderFailure>(new ResponderFailure(status, message));

    private static Result<InvitationResponse, ResponderFailure> Refused(int status, string message) =>
        Result.Failure<InvitationResponse, ResponderFailure>(new ResponderFailure(status, message));
}
