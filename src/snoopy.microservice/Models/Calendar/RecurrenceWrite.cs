namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// The rule as the editor states it. <c>Frequency</c> is DAILY, WEEKLY, MONTHLY or YEARLY;
/// <c>ByDay</c> holds "MO".."SU" for a weekly rule; a monthly rule names either a day of the month
/// (<c>ByMonthDay</c>) or "the 2nd Tuesday" (<c>BySetPos</c> with <c>BySetPosDay</c>).
/// <c>Until</c> is the last day an instance may fall on, in the event's own zone.
/// </summary>
public sealed record RecurrenceWrite(
    string Frequency,
    int Interval,
    IReadOnlyList<string> ByDay,
    int? ByMonthDay,
    int? BySetPos,
    string? BySetPosDay,
    RecurrenceEnd End,
    int? Count,
    DateOnly? Until);
