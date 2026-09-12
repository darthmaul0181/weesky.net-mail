using weesky.Snoopy.Microservice.Models.Mail;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

internal sealed record InvitationAttendee(string Email, string? Name, string? PartStat);

/// <summary>A received calendar part, read once. <see cref="DtStartLine"/> is the file's own
/// DTSTART line, unfolded, which the REPLY copies verbatim so the organizer's agenda pairs it.</summary>
internal sealed record ParsedInvitation(
    InvitationMethod Method, string Uid, int Sequence, bool OccurrenceOnly,
    string? Summary, string? Location, bool Repeats, bool IsAllDay,
    DateTime? Start, DateTime? End, DateOnly? StartDate, DateOnly? EndDateExclusive,
    InvitationPerson? Organizer, IReadOnlyList<InvitationAttendee> Attendees,
    string? DtStartLine);

/// <summary><see cref="Ignored"/>: no handled METHOD, the part stays an attachment.
/// <see cref="Reason"/>: the guards or the parser refused it — « Invitation illisible ».</summary>
internal sealed record InvitationReading(ParsedInvitation? Invitation, bool Ignored, string? Reason)
{
    internal static InvitationReading Unreadable(string reason) => new(null, false, reason);
    internal static readonly InvitationReading NotAnInvitation = new(null, true, null);
}

/// <summary>Pure: the calendar part's text to what the card and the responder need. The file is
/// judged by the same guards a PUT faces (décision 1), METHOD first so a PUBLISH that would fail
/// them is ignored rather than declared unreadable.</summary>
internal static class InvitationParser
{
    internal const string Unparsable = "The calendar part could not be parsed";

    internal static InvitationReading Read(string ics)
    {
        if (IcsGuards.CheckSize(ics) is { } tooLarge) return InvitationReading.Unreadable(tooLarge.Message);
        var parsed = IcsDocument.TryLoad(ics);
        if (parsed is null) return InvitationReading.Unreadable(Unparsable);

        var method = parsed.Method?.Trim().ToUpperInvariant() switch
        {
            "REQUEST" => InvitationMethod.Request,
            "CANCEL" => InvitationMethod.Cancel,
            _ => (InvitationMethod?)null,
        };
        if (method is null) return InvitationReading.NotAnInvitation;
        // The guards refuse a stored resource carrying METHOD (RFC 4791 § 4.1), and the body that
        // will be stored is this one without it — what PartStatRewriter.Rewrite produces.
        parsed.Method = null;
        if (IcsGuards.CheckParsed(ics, parsed) is { } refused) return InvitationReading.Unreadable(refused.Message);

        var master = IcsDocument.MasterOf(parsed);
        var component = master ?? IcsDocument.Components(parsed).First();
        var projection = IcsProjector.Project(parsed, IcsTimeZones.Utc);
        var allDay = component.DtStart is { HasTime: false };

        return new InvitationReading(new ParsedInvitation(
            method.Value,
            component.Uid ?? string.Empty,
            component.Sequence,
            master is null,
            projection.Summary,
            projection.Location,
            master is not null && IcsDocument.Repeats(master),
            allDay,
            allDay ? null : projection.StartsAt,
            allDay ? null : projection.EndsAt,
            allDay ? DateOnly.FromDateTime(component.DtStart!.Value) : null,
            allDay && IcsDocument.EndOf(component) is { } end ? DateOnly.FromDateTime(end.Value) : null,
            component.Organizer is { } organizer && IcsProjector.Address(organizer.Value) is { } email
                ? new InvitationPerson(email, Text(organizer.CommonName)) : null,
            [.. (component.Attendees ?? []).Where(a => a is not null)
                .Select(a => (Address: IcsProjector.Address(a.Value), Attendee: a))
                .Where(x => x.Address is not null)
                .Select(x => new InvitationAttendee(x.Address!, Text(x.Attendee.CommonName), Upper(x.Attendee.ParticipationStatus)))],
            PartStatRewriter.LineOf(ics, "DTSTART")), false, null);
    }

    /// <summary>The master's SEQUENCE of a stored file; 0 when the file carries none or cannot be read.</summary>
    internal static int SequenceOf(string ics) =>
        IcsDocument.TryLoad(ics) is { } parsed && IcsDocument.MasterOf(parsed) is { } master ? master.Sequence : 0;

    /// <summary>The PARTSTAT the file's master carries for an address, case-insensitively; null when the address is not invited.</summary>
    internal static string? PartStatOf(string ics, string address)
    {
        if (IcsDocument.TryLoad(ics) is not { } parsed) return null;
        var component = IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).FirstOrDefault();
        return component?.Attendees?
            .FirstOrDefault(a => a is not null && string.Equals(IcsProjector.Address(a.Value), address, StringComparison.OrdinalIgnoreCase))
            is { } attendee ? Upper(attendee.ParticipationStatus) ?? "NEEDS-ACTION" : null;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Upper(string? value) => Text(value)?.ToUpperInvariant();
}
