namespace weesky.Snoopy.Microservice.Models.Mail;

public enum InvitationMethod { Request, Cancel }

/// <summary>What the calendar holds for the received UID (spec 5e, décision 3).</summary>
public enum InvitationPresence { Absent, Current, Outdated, Newer, Cancelled }

public sealed record InvitationPerson(string Email, string? Name);

/// <summary>
/// The <c>invitation</c> block of a message detail, and the answer of the respond endpoint. Filled
/// when the message carries a calendar part whose METHOD is REQUEST or CANCEL; <see cref="Unreadable"/>
/// when that part fails the guards every stored file passes, in which case only <see cref="Part"/>
/// and <see cref="Reason"/> are set.
/// </summary>
public sealed class MailInvitation
{
    public InvitationMethod Method { get; init; }
    public string Uid { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public string? Summary { get; init; }
    /// <summary>UTC, like the API's occurrences; null on a whole-day invitation.</summary>
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
    /// <summary>Set on a whole-day invitation; the end is exclusive, as the occurrences spell it.</summary>
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDateExclusive { get; init; }
    public bool IsAllDay { get; init; }
    public string? Location { get; init; }
    public bool Repeats { get; init; }
    public InvitationPerson? Organizer { get; init; }
    public IReadOnlyList<InvitationPerson> Attendees { get; init; } = [];
    /// <summary>The user's address among the ATTENDEEs, or null when the invitation was forwarded (décision 2).</summary>
    public string? AddressedTo { get; init; }
    /// <summary>The PARTSTAT the received file carries for the user — NEEDS-ACTION on a fresh invitation.</summary>
    public string? FilePartStat { get; init; }
    /// <summary>The PARTSTAT the stored file carries for the user — the answer given; null when not in the calendar.</summary>
    public string? SavedPartStat { get; init; }
    public InvitationPresence InCalendar { get; init; }
    public Guid? CalendarId { get; init; }
    /// <summary>The file targets one date of a series (RECURRENCE-ID without a master): shown, never applied (décision 1 bis).</summary>
    public bool OccurrenceOnly { get; init; }
    /// <summary>The MIME part specifier the respond endpoint re-reads the file by (décision 4).</summary>
    public string Part { get; init; } = string.Empty;
    public bool Unreadable { get; init; }
    public string? Reason { get; init; }
}
