using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>Décision 12: the guest's PARTSTAT and the REPLY's DTSTAMP into their ATTENDEE lines of the
/// stored file, by the PUT path under the stored name and only over the version that was judged — a
/// change or a deletion in between is a conflict, never overwritten nor re-created. An answer the file
/// already holds is not written again. It never sends a mail nor calls the scheduler: the shape does not move.</summary>
internal sealed class InvitationReplyApplier(
    InvitationPartLoader parts, InvitationReader reader, IDavCalendarWriter writer, ILogger<InvitationReplyApplier> logger)
    : IInvitationReplyApplier
{
    internal const string NotAReply = "reply_not_a_reply";
    internal const string NotApplicable = "reply_not_applicable";

    public async Task<Result<ApplyReplyResponse, ResponderFailure>> ApplyAsync(
        User user, MailAccountConnection connection, ApplyReplyRequest request, CancellationToken cancellationToken)
    {
        var ics = await parts.LoadAsync(user, connection, request.Folder, request.Uid, request.Part, cancellationToken);
        if (ics.IsFailure) return Result.Failure<ApplyReplyResponse, ResponderFailure>(ics.Error);
        if (InvitationParser.Read(ics.Value).Invitation is not { Method: InvitationMethod.Reply } parsed)
            return Refused(NotAReply);

        var context = await reader.ResolveReplyAsync(user, parsed, cancellationToken);
        if (context is not { Reply: { Status: ReplyStatus.Applicable } reply, Stored: { } stored }) return Refused(NotApplicable);
        if (reply.Applied) return Answered(parsed, context, request, null);

        var rewritten = PartStatRewriter.Rewrite(stored.IcsRaw, reply.Email, reply.PartStat, parsed.DtStamp);
        if (rewritten is null) return Refused(NotApplicable);
        var outcome = await writer.PutAsync(user.WebmailUid, stored.CalendarId, stored.DavName, rewritten,
            cancellationToken, ifMatch: DavPropertyTables.EntityTag(stored.IcsHash), cause: RevisionCause.Webmail);
        if (outcome.Status is not (DavWriteStatus.Created or DavWriteStatus.Replaced))
        {
            logger.LogWarning("The reply to {Uid} was not applied to {DavName}: {Status}", parsed.Uid, stored.DavName, outcome.Status);
            return Answered(parsed, context, request, InvitationResponder.CodeOf(outcome.Status));
        }

        return Answered(parsed, await reader.ResolveReplyAsync(user, parsed, cancellationToken), request, null);
    }

    private static Result<ApplyReplyResponse, ResponderFailure> Answered(
        ParsedInvitation parsed, InvitationContext context, ApplyReplyRequest request, string? error) =>
        Result.Success<ApplyReplyResponse, ResponderFailure>(
            new ApplyReplyResponse(InvitationReader.Block(parsed, context, request.Part), error is null, error));

    private static Result<ApplyReplyResponse, ResponderFailure> Refused(string code) =>
        Result.Failure<ApplyReplyResponse, ResponderFailure>(new ResponderFailure(400, code));
}
