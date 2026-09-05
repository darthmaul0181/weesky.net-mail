using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// The one door in and out of the iCalendar object model. Nothing else in the calendar slices
/// parses or writes iCalendar text.
/// </summary>
internal static class IcsDocument
{
    private const string DateFormat = "yyyyMMdd";
    private const string InstantFormat = "yyyyMMdd'T'HHmmss";

    /// <summary>Null when the text is not a calendar. Ical.Net answers null on an empty body and
    /// throws on a malformed one; both are the same non-answer to a caller.</summary>
    internal static IcsCalendar? TryLoad(string ics)
    {
        if (string.IsNullOrWhiteSpace(ics)) return null;
        try { return IcsCalendar.Load(ics); }
        catch (Exception) { return null; }
    }

    internal static string Serialize(IcsCalendar calendar) => new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;

    internal static string HashOf(string ics) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ics)));

    internal static CalendarEvent? MasterOf(IcsCalendar calendar) =>
        Components(calendar).FirstOrDefault(e => e.RecurrenceIdentifier is null);

    /// <summary>Document order, and free of the nulls the library's typed lists admit.</summary>
    internal static IEnumerable<CalendarEvent> Components(IcsCalendar calendar) =>
        calendar.Children.OfType<CalendarEvent>();

    /// <summary>The RECURRENCE-ID as the file spells it — "" for a master, since that is the key
    /// an override-less component holds in <c>calendar_attendees</c>.</summary>
    internal static string InstanceIdOf(CalendarEvent component) =>
        component.RecurrenceIdentifier?.StartTime is { } at ? LiteralOf(at) : string.Empty;

    /// <summary>DTEND when the component gives one, else DTSTART advanced by the duration RFC 5545
    /// implies — a day for a date, nothing for a time — in the component's own frame, so that a
    /// span crossing a transition keeps its wall-clock length.</summary>
    internal static CalDateTime? EndOf(CalendarEvent component)
    {
        if (component.DtEnd is { } dtEnd) return dtEnd;
        if (component.DtStart is not { } dtStart) return null;
        // A component whose DURATION the library refuses to read lasts nothing rather than nothing
        // at all: an end before its own start would leave the row out of every window query.
        Duration span;
        try { span = component.EffectiveDuration; }
        catch (Exception) { return dtStart; }

        var local = dtStart.Value
            .AddDays(span.Sign * (7 * (span.Weeks ?? 0) + (span.Days ?? 0)))
            .AddHours(span.Sign * (span.Hours ?? 0))
            .AddMinutes(span.Sign * (span.Minutes ?? 0))
            .AddSeconds(span.Sign * (span.Seconds ?? 0));
        return new CalDateTime(local, dtStart.TzId, dtStart.HasTime);
    }

    /// <summary>A moment as iCalendar spells it: a bare date, a local reading, or one with the Z a
    /// UTC value carries. The form a RECURRENCE-ID has to repeat to address that instance.</summary>
    internal static string LiteralOf(CalDateTime at) => at switch
    {
        { HasTime: false } => at.Value.ToString(DateFormat, CultureInfo.InvariantCulture),
        { IsUtc: true } => at.Value.ToString(InstantFormat, CultureInfo.InvariantCulture) + "Z",
        _ => at.Value.ToString(InstantFormat, CultureInfo.InvariantCulture),
    };

    /// <summary>The inverse of <see cref="LiteralOf"/>: the instant an instance id names, posed in
    /// the master's own DTSTART form. Null when the literal is not spelled in that form.</summary>
    internal static CalDateTime? InstanceOf(CalendarEvent master, string instanceId)
    {
        if (master.DtStart is not { } start) return null;
        if (!start.HasTime)
            return DateOnly.TryParseExact(instanceId, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? new CalDateTime(date)
                : null;

        // Exchange writes a zoned series' RECURRENCE-ID in Z form: the instant is read as one and
        // put back in the master's own zone, which is the only form the rest of the module spells.
        var instant = instanceId.EndsWith('Z') && (start.IsUtc || IcsTimeZones.ResolveIana(start.TzId) is not null);
        var literal = instant ? instanceId[..^1] : instanceId;
        if (!DateTime.TryParseExact(literal, InstantFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;
        if (start.IsUtc) return new CalDateTime(DateTime.SpecifyKind(local, DateTimeKind.Utc), IcsTimeZones.Utc, true);
        return instant && IcsTimeZones.ResolveIana(start.TzId) is { } iana
            ? new CalDateTime(IcsTimeZones.FromUtc(DateTime.SpecifyKind(local, DateTimeKind.Utc), iana), start.TzId, true)
            : new CalDateTime(local, start.TzId, true);
    }
}
