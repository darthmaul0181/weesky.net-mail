using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>What the base says about a received invitation: who the user is in it, and what the
/// calendar holds for its UID (décisions 2 and 3).</summary>
internal sealed record InvitationContext(
    string? AddressedTo, string? FilePartStat, StoredEventRef? Stored, int SavedSequence,
    string? SavedPartStat, InvitationPresence Presence);

public interface IInvitationReader
{
    /// <summary>The <c>invitation</c> block for a downloaded calendar part; null when the part is
    /// not a REQUEST or CANCEL, so the file stays an attachment.</summary>
    Task<MailInvitation?> ReadAsync(User user, MailAccountConnection connection, MailCalendarPart part, CancellationToken cancellationToken);
}

internal sealed class InvitationReader(IUserAddresses addresses, ICalendarEventStore events) : IInvitationReader
{
    internal const string TooLarge = "The calendar part is larger than an event may be";

    public async Task<MailInvitation?> ReadAsync(
        User user, MailAccountConnection connection, MailCalendarPart part, CancellationToken cancellationToken)
    {
        if (part.TooLarge) return new MailInvitation { Part = part.Part, Unreadable = true, Reason = TooLarge };
        var reading = InvitationParser.Read(part.Ics);
        if (reading.Ignored) return null;
        if (reading.Invitation is not { } parsed)
            return new MailInvitation { Part = part.Part, Unreadable = true, Reason = reading.Reason };

        return Block(parsed, await ResolveAsync(user, connection, parsed, cancellationToken), part.Part);
    }

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
        Part = part,
    };
}
