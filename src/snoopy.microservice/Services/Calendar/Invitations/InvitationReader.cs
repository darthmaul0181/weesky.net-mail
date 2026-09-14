using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>What the base says about a received invitation: who the user is in it, and what the
/// calendar holds for its UID (décisions 2 and 3); on a REPLY, the guest's answer (décision 12).</summary>
internal sealed record InvitationContext(
    string? AddressedTo, string? FilePartStat, StoredEventRef? Stored, int SavedSequence,
    string? SavedPartStat, InvitationPresence Presence, InvitationReply? Reply = null);

public interface IInvitationReader
{
    /// <summary>The <c>invitation</c> block for a downloaded calendar part; null when the part is
    /// not a REQUEST, CANCEL or REPLY, so the file stays an attachment.</summary>
    Task<MailInvitation?> ReadAsync(User user, MailAccountConnection connection, MailCalendarPart part, CancellationToken cancellationToken);
}

internal sealed class InvitationReader(IUserAddresses addresses, ICalendarEventStore events) : IInvitationReader
{
    internal const string TooLarge = "The calendar part is larger than an event may be";
    internal const string ReplyWithoutAttendee = "reply_without_attendee";

    public async Task<MailInvitation?> ReadAsync(
        User user, MailAccountConnection connection, MailCalendarPart part, CancellationToken cancellationToken)
    {
        if (part.TooLarge) return new MailInvitation { Part = part.Part, Unreadable = true, Reason = TooLarge };
        var reading = InvitationParser.Read(part.Ics);
        if (reading.Ignored) return null;
        if (reading.Invitation is not { } parsed)
            return new MailInvitation { Part = part.Part, Unreadable = true, Reason = reading.Reason };
        if (parsed.Method is not InvitationMethod.Reply)
            return Block(parsed, await ResolveAsync(user, connection, parsed, cancellationToken), part.Part);

        return parsed.Attendees.Count == 0
            ? new MailInvitation { Part = part.Part, Unreadable = true, Reason = ReplyWithoutAttendee }
            : Block(parsed, await ResolveReplyAsync(user, parsed, cancellationToken), part.Part);
    }

    /// <summary>Décision 12: a guest's answer is applicable only to an event the webmail invited, at
    /// the version it holds, from a guest it lists, for the whole series, saying yes, maybe or no, and
    /// not older than the answer the file holds — RFC 5546 orders the replies of one SEQUENCE by DTSTAMP.
    /// Reading it writes nothing; it is applied once every line the rewrite touches holds it.</summary>
    internal async Task<InvitationContext> ResolveReplyAsync(User user, ParsedInvitation parsed, CancellationToken cancellationToken)
    {
        if (parsed.Attendees.Count == 0) return new InvitationContext(null, null, null, 0, null, InvitationPresence.Absent);
        if (parsed.OccurrenceOnly) return Unmatched(parsed, ReplyStatus.OccurrenceOnly);
        var rows = await events.FindByUidAsync(user.WebmailUid, parsed.Uid, cancellationToken);
        var stored = rows.FirstOrDefault(r => r.SchedulingOwner == SchedulingDecider.WebmailOwner) ?? rows.FirstOrDefault();
        if (stored is null) return Unmatched(parsed, ReplyStatus.UnknownUid);

        var file = IcsDocument.TryLoad(stored.IcsRaw);
        IReadOnlyList<GuestLine> LinesOf(string email) => file is null ? [] : InvitationParser.GuestLinesOf(file, email);
        // A delegation REPLY carries the delegate too: the guest is the one the file invited.
        var match = parsed.Attendees.Select(a => (Attendee: a, Lines: LinesOf(a.Email))).FirstOrDefault(x => x.Lines.Count > 0);
        var (who, lines) = match.Attendee is null ? (parsed.Attendees[0], []) : match;
        var savedSequence = file is null ? 0 : InvitationParser.SequenceOf(file);
        var partStat = who.PartStat ?? "NEEDS-ACTION";
        var presence = parsed.Sequence < savedSequence ? InvitationPresence.Newer : InvitationPresence.Current;
        int StampOrder(GuestLine line) => parsed.DtStamp is { } received && line.Stamp is { } held ? string.CompareOrdinal(held, received) : 0;

        var status = stored.SchedulingOwner != SchedulingDecider.WebmailOwner ? ReplyStatus.NotOwner
            : presence is InvitationPresence.Newer ? ReplyStatus.Stale
            : lines.Count == 0 ? ReplyStatus.UnknownAttendee
            : partStat is not ("ACCEPTED" or "TENTATIVE" or "DECLINED") ? ReplyStatus.UnsupportedAnswer
            : parsed.Sequence == savedSequence && lines.Any(l => StampOrder(l) > 0) ? ReplyStatus.Superseded
            : ReplyStatus.Applicable;
        // An older stamp on the same answer is not applied yet: the later stamp must reach the file,
        // or an answer between the two would pass for newer than both.
        var applied = status is ReplyStatus.Applicable && lines.All(l => l.PartStat == partStat && StampOrder(l) >= 0);
        return new InvitationContext(null, null, stored, savedSequence, lines.FirstOrDefault()?.PartStat, presence,
            ReplyOf(who, status, applied));
    }

    private static InvitationContext Unmatched(ParsedInvitation parsed, ReplyStatus status) =>
        new(null, null, null, 0, null, InvitationPresence.Absent, ReplyOf(parsed.Attendees[0], status));

    private static InvitationReply ReplyOf(InvitationAttendee who, ReplyStatus status, bool applied = false) =>
        new(who.Email.Trim().ToLowerInvariant(), who.Name, who.PartStat ?? "NEEDS-ACTION", status, applied);

    internal async Task<InvitationContext> ResolveAsync(
        User user, MailAccountConnection connection, ParsedInvitation parsed, CancellationToken cancellationToken)
    {
        // The user's list decides the order: the first of HIS addresses that is invited answers.
        var mine = await addresses.ForAccountAsync(user, connection, cancellationToken);
        var addressedTo = mine.FirstOrDefault(a => parsed.Attendees.Any(
            x => string.Equals(x.Email, a, StringComparison.OrdinalIgnoreCase)));
        var filePartStat = addressedTo is null ? null
            : parsed.Attendees.First(x => string.Equals(x.Email, addressedTo, StringComparison.OrdinalIgnoreCase))
                .PartStat ?? "NEEDS-ACTION";

        // An occurrence alone is shown and never applied: the calendar is not even asked (1 bis).
        if (parsed.OccurrenceOnly)
            return new InvitationContext(addressedTo, filePartStat, null, 0, null, InvitationPresence.Absent);

        var stored = (await events.FindByUidAsync(user.WebmailUid, parsed.Uid, cancellationToken)).FirstOrDefault();
        if (stored is null)
            return new InvitationContext(addressedTo, filePartStat, null, 0, null, InvitationPresence.Absent);

        var savedSequence = InvitationParser.SequenceOf(stored.IcsRaw);
        var savedPartStat = addressedTo is null ? null : InvitationParser.PartStatOf(stored.IcsRaw, addressedTo);
        var presence = parsed.Sequence < savedSequence ? InvitationPresence.Newer
            : parsed.Method is InvitationMethod.Cancel ? InvitationPresence.Cancelled
            : parsed.Sequence > savedSequence ? InvitationPresence.Outdated
            : InvitationPresence.Current;
        return new InvitationContext(addressedTo, filePartStat, stored, savedSequence, savedPartStat, presence);
    }

    internal static MailInvitation Block(ParsedInvitation parsed, InvitationContext context, string part) => new()
    {
        Method = parsed.Method,
        Uid = parsed.Uid,
        Sequence = parsed.Sequence,
        Summary = parsed.Summary,
        Start = parsed.Start,
        End = parsed.End,
        StartDate = parsed.StartDate,
        EndDateExclusive = parsed.EndDateExclusive,
        IsAllDay = parsed.IsAllDay,
        Location = parsed.Location,
        Repeats = parsed.Repeats,
        Organizer = parsed.Organizer,
        Attendees = [.. parsed.Attendees.Select(a => new InvitationPerson(a.Email, a.Name))],
        AddressedTo = context.AddressedTo,
        FilePartStat = context.FilePartStat,
        SavedPartStat = context.SavedPartStat,
        InCalendar = context.Presence,
        CalendarId = context.Stored?.CalendarId,
        OccurrenceOnly = parsed.OccurrenceOnly,
        Reply = context.Reply,
        Part = part,
    };
}
