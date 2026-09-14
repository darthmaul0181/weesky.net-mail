using Ical.Net.CalendarComponents;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>What an invitee has to be told about: dates, rule, status, title, place and the
/// guest list — on every component, each under its own RECURRENCE-ID (décision 9). A PARTSTAT
/// or an address's case changes nothing: an Outlook REPLY rewrites both.</summary>
/// <remarks>Every <c>string</c> overload parses its own text; <see cref="SchedulingDecider"/>
/// parses once and calls the <see cref="IcsCalendar"/> overloads directly.</remarks>
internal static class SchedulingShape
{
    internal static string? Of(string ics, bool withAttendees = true)
    {
        var parsed = IcsDocument.TryLoad(ics);
        return parsed is null ? null : Of(parsed, withAttendees);
    }

    internal static string Of(IcsCalendar parsed, bool withAttendees = true)
    {
        var parts = IcsDocument.Components(parsed)
            .Select(c => (Key: IcsDocument.InstanceIdOf(c), Text: ComponentShape(c, withAttendees)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => Prefixed(p.Key) + p.Text);
        return string.Join("\n", parts);
    }

    internal static string HashOf(string shape) => IcsDocument.HashOf(shape);

    internal static IReadOnlySet<string> Addresses(string ics)
    {
        var parsed = IcsDocument.TryLoad(ics);
        return parsed is null ? new HashSet<string>() : Addresses(parsed);
    }

    internal static IReadOnlySet<string> Addresses(IcsCalendar parsed) =>
        IcsDocument.Components(parsed).SelectMany(AddressesOf).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every component's own attendee addresses, by instance key — what
    /// <see cref="SchedulingDecider"/> compares per component rather than as one flat set.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlySet<string>> AddressesByComponent(IcsCalendar parsed) =>
        ComponentsByKey(parsed).ToDictionary(
            kv => kv.Key, kv => (IReadOnlySet<string>)AddressesOf(kv.Value).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

    internal static string? OrganizerOf(string ics)
    {
        var parsed = IcsDocument.TryLoad(ics);
        return parsed is null ? null : OrganizerOf(parsed);
    }

    internal static string? OrganizerOf(IcsCalendar parsed) =>
        IcsDocument.MasterOf(parsed)?.Organizer is { } o ? IcsProjector.Address(o.Value)?.Trim().ToLowerInvariant() : null;

    /// <summary>The components a client changed without versioning the change, each with the SEQUENCE
    /// the invitees hold of it: an existing component's own before, or the master's for a new override
    /// that neither passed it nor came with a master that advanced (décision 9).</summary>
    internal static IReadOnlyDictionary<string, int> ChangedWithoutBump(string before, string after)
    {
        var lagging = new Dictionary<string, int>(StringComparer.Ordinal);
        var earlier = ComponentsByKey(before);
        var later = ComponentsByKey(after);
        if (earlier is null || later is null) return lagging;
        var masterBefore = earlier.GetValueOrDefault("")?.Sequence ?? 0;
        var masterAdvanced = (later.GetValueOrDefault("")?.Sequence ?? 0) > masterBefore;
        foreach (var (key, now) in later)
        {
            if (earlier.TryGetValue(key, out var was))
            {
                if (now.Sequence <= was.Sequence && ComponentShape(was, true) != ComponentShape(now, true)) lagging[key] = was.Sequence;
            }
            else if (key.Length > 0 && !masterAdvanced && now.Sequence <= masterBefore) lagging[key] = masterBefore;
        }
        return lagging;
    }

    private static Dictionary<string, CalendarEvent>? ComponentsByKey(string ics)
    {
        var parsed = IcsDocument.TryLoad(ics);
        return parsed is null ? null : ComponentsByKey(parsed);
    }

    private static Dictionary<string, CalendarEvent> ComponentsByKey(IcsCalendar parsed) =>
        IcsDocument.Components(parsed).GroupBy(IcsDocument.InstanceIdOf, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>Length-prefixed, not '|'-joined: a title or a place is free text under no one's
    /// control, and "a|b" title + no location must never read the same as "a" title + "b|" location.</summary>
    private static string ComponentShape(CalendarEvent c, bool withAttendees)
    {
        var addresses = withAttendees ? string.Join(",", AddressesOf(c).Order(StringComparer.Ordinal)) : "";
        return Prefixed(IcsComposer.Shape(c)) + Prefixed(c.Summary ?? "") + Prefixed(c.Location ?? "") + Prefixed(addresses);
    }

    /// <summary>A netstring-style length prefix: self-delimiting however many ':' or other
    /// separators the value itself holds, so two different fields can never concatenate alike.</summary>
    private static string Prefixed(string value) => value.Length + ":" + value;

    private static IEnumerable<string> AddressesOf(CalendarEvent c) => (c.Attendees ?? [])
        .Where(a => a is not null)
        .Select(a => IcsProjector.Address(a.Value))
        .OfType<string>()
        .Select(a => a.Trim().ToLowerInvariant());
}
