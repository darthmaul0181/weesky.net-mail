using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// Fonctionnalité 6: it is the grouping by UID that makes the resource. One imported VCALENDAR
/// holding twelve events is twelve CalDAV resources, each a whole VCALENDAR carrying only the
/// VTIMEZONEs its own components cite — and none of the envelope's METHOD, X-WR-CALNAME or colour,
/// which describe the file, not the event.
/// </summary>
internal static class IcsResources
{
    /// <summary>VTODO and VJOURNAL are counted, not carried: the collection stores VEVENT only.</summary>
    internal sealed record SplitOutcome(IReadOnlyList<string> Resources, int IgnoredTodos, int IgnoredJournals);

    internal static SplitOutcome Split(string vcalendar)
    {
        var parsed = IcsDocument.TryLoad(vcalendar);
        if (parsed is null) return new SplitOutcome([], 0, 0);

        var order = new List<IcsCalendar>();
        var byUid = new Dictionary<string, IcsCalendar>(StringComparer.Ordinal);
        foreach (var component in IcsDocument.Components(parsed).ToList())
        {
            // Ical.Net gives a component with no UID one of its own, which groups it alone — the
            // key the store then writes into the resource it creates.
            var uid = component.Uid ?? string.Empty;
            if (!byUid.TryGetValue(uid, out var resource))
            {
                resource = new IcsCalendar();
                byUid[uid] = resource;
                order.Add(resource);
            }

            resource.AddChild(component);
        }

        foreach (var resource in order)
            foreach (var tzid in ZonesCited(resource).ToList())
                if (parsed.TimeZones.FirstOrDefault(z => z?.TzId == tzid) is { } zone)
                    resource.AddTimeZone(zone);

        return new SplitOutcome(order.Select(IcsDocument.Serialize).ToList(), parsed.Todos.Count, parsed.Journals.Count);
    }

    private static IEnumerable<string> ZonesCited(IcsCalendar resource) =>
        IcsDocument.Components(resource).SelectMany(Dates)
            .Select(d => d?.TzId)
            .Where(id => !string.IsNullOrEmpty(id) && id != IcsTimeZones.Utc)
            .Distinct(StringComparer.Ordinal)!;

    private static IEnumerable<CalDateTime?> Dates(CalendarEvent component) =>
        new[] { component.DtStart, component.DtEnd, component.RecurrenceIdentifier?.StartTime }
            .Concat(component.RecurrenceDates?.GetAllDates() ?? [])
            .Concat(component.ExceptionDates?.GetAllDates() ?? []);
}
