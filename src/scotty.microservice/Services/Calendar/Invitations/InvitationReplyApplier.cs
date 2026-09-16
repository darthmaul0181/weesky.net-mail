using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Dav;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Dav;

namespace weesky.Scotty.Microservice.Services.Calendar.Invitations;

/// <summary>Décision 12: the guest's PARTSTAT and the REPLY's DTSTAMP into their ATTENDEE lines of the
/// stored file, by the PUT path under the stored name and only over the version that was judged — a
/// change or a deletion in between is a conflict, never overwritten nor re-created. An answer the file
/// already holds is not written again. It never sends a mail nor calls the scheduler: the shape does not move.</summary>
internal sealed class InvitationReplyApplier(
    InvitationPartLoader parts, InvitationReader reader, IDavCalendarWriter writer, ILogger<InvitationReplyApplier> logger)
    : IInvitationReplyApplier, IDeliveryReplyApplier
{
    internal const string NotAReply = "reply_not_a_reply";
    internal const string NotApplicable = "reply_not_applicable";
    /// <summary>The stored file's ATTENDEE line the reader matched cannot be rewritten by
    /// <see cref="PartStatRewriter.Rewrite"/> — kept distinct from <see cref="NotApplicable"/>
    /// internally so the projection can tell the two apart (spec 5e3), but the IMAP door below
    /// still answers <see cref="NotApplicable"/> for it: its contract does not change.</summary>
    internal const string AttendeeLineMissing = "reply_attendee_line_missing";

    public async Task<Result<ApplyReplyResponse, ResponderFailure>> ApplyAsync(
        User user, MailAccountConnection connection, ApplyReplyRequest request, CancellationToken cancellationToken)
    {
        var ics = await parts.LoadAsync(user, connection, request.Folder, request.Uid, request.Part, cancellationToken);
        if (ics.IsFailure) return Result.Failure<ApplyReplyResponse, ResponderFailure>(ics.Error);

        var outcome = await ApplyIcsAsync(user, ics.Value, RevisionCause.Webmail, cancellationToken);
        if (outcome.Refusal is { } refusal)
            return Result.Failure<ApplyReplyResponse, ResponderFailure>(
                new ResponderFailure(400, refusal == AttendeeLineMissing ? NotApplicable : refusal));
        var error = outcome.WriteStatus is { } status && !outcome.Applied ? InvitationResponder.CodeOf(status) : null;
        return Result.Success<ApplyReplyResponse, ResponderFailure>(
            new ApplyReplyResponse(InvitationReader.Block(outcome.Parsed!, outcome.Context!, request.Part), error is null, error));
    }

    async Task<DeliveryReplyResponse> IDeliveryReplyApplier.ApplyAsync(User user, string ics, CancellationToken cancellationToken)
    {
        var outcome = await ApplyIcsAsync(user, ics, RevisionCause.Delivery, cancellationToken);
        var uid = outcome.Parsed?.Uid;
        return outcome switch
        {
            { Refusal: NotAReply } => new(DeliveryReplyOutcome.NotAReply, null, null),
            // A REPLY carrying no ATTENDEE at all (InvitationReader.ResolveReplyAsync leaves
            // Reply null): the spec's outcome table calls this notAReply, not notApplicable.
            { Refusal: NotApplicable, Context.Reply: null } => new(DeliveryReplyOutcome.NotAReply, null, null),
            { Refusal: AttendeeLineMissing } => new(DeliveryReplyOutcome.NotApplicable, uid, "attendee_line_missing"),
            { Refusal: not null } => new(DeliveryReplyOutcome.NotApplicable, uid, outcome.Context?.Reply?.Status.ToString()),
            { AlreadyApplied: true } => new(DeliveryReplyOutcome.AlreadyApplied, uid, null),
            { Applied: true } => new(DeliveryReplyOutcome.Applied, uid, null),
            _ => new(DeliveryReplyOutcome.Conflict, uid, outcome.WriteStatus?.ToString()),
        };
    }

    /// <summary>The one application path, whichever door brought the text (spec 5e3, décision 2).</summary>
    internal async Task<IcsReplyOutcome> ApplyIcsAsync(User user, string ics, RevisionCause cause, CancellationToken cancellationToken)
    {
        if (InvitationParser.Read(ics).Invitation is not { Method: InvitationMethod.Reply } parsed)
            return new IcsReplyOutcome(null, null, false, false, null, NotAReply);

        var context = await reader.ResolveReplyAsync(user, parsed, cancellationToken);
        if (context is not { Reply: { Status: ReplyStatus.Applicable } reply, Stored: { } stored })
            return new IcsReplyOutcome(parsed, context, false, false, null, NotApplicable);
        if (reply.Applied) return new IcsReplyOutcome(parsed, context, false, true, null, null);

        var rewritten = PartStatRewriter.Rewrite(stored.IcsRaw, reply.Email, reply.PartStat, parsed.DtStamp);
        if (rewritten is null) return new IcsReplyOutcome(parsed, context, false, false, null, AttendeeLineMissing);
        var outcome = await writer.PutAsync(user.WebmailUid, stored.CalendarId, stored.DavName, rewritten,
            cancellationToken, ifMatch: DavPropertyTables.EntityTag(stored.IcsHash), cause: cause);
        if (outcome.Status is not (DavWriteStatus.Created or DavWriteStatus.Replaced))
        {
            logger.LogWarning("The reply to {Uid} was not applied to {DavName}: {Status}",
                LogText.Safe(parsed.Uid), stored.DavName, outcome.Status);
            return new IcsReplyOutcome(parsed, context, false, false, outcome.Status, null);
        }

        return new IcsReplyOutcome(parsed, await reader.ResolveReplyAsync(user, parsed, cancellationToken), true, false, outcome.Status, null);
    }
}
