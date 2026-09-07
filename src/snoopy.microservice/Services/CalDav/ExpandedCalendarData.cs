using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The expanded form of <c>calendar-data</c>, RFC 4791 § 9.6.5: one VEVENT per instance of
/// <c>[from, to[</c>, each with its RECURRENCE-ID and its times in UTC, without RRULE, RDATE,
/// EXDATE or VTIMEZONE; the master's alarms ride into every instance, and an override replaces the
/// instance it names. <see cref="OccurrenceExpander"/> walks and <see cref="IcsComposer"/> writes:
/// nothing here recomputes an instant.
/// </summary>
internal static class ExpandedCalendarData
{
    /// <exception cref="DavPreconditionException"><c>max-instances</c> past the expander's own
    /// cap — never a document quietly missing the rest; <c>valid-calendar-data</c> on a stored
    /// file that no longer parses.</exception>
    internal static string Expand(string icsRaw, DateTime fromUtc, DateTime toUtc, string calendarTimeZone)
    {
        var parsed = IcsDocument.TryLoad(icsRaw)
            ?? throw new DavPreconditionException(CalDavError.ValidCalendarData);
        var occurrences = OccurrenceExpander.Expand(Guid.Empty, Guid.Empty, parsed, fromUtc, toUtc,
            calendarTimeZone, calendarTimeZone);
        if (occurrences.Count >= OccurrenceExpander.CapFor(fromUtc, toUtc))
            throw new DavPreconditionException(CalDavError.MaxInstances);

        var components = IcsDocument.Components(parsed).ToList();
        var expanded = IcsComposer.Envelope();
        foreach (var occurrence in occurrences)
        {
            expanded.Events.Add(IcsComposer.Instance(parsed, occurrence,
                OccurrenceExpander.SourceOf(occurrence, components)!, calendarTimeZone));
        }

        return IcsDocument.Serialize(expanded);
    }
}
