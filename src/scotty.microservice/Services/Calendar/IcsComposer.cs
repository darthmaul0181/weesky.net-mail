using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Scotty.Microservice.Models.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Scotty.Microservice.Services.Calendar;

/// <summary>
/// The write half of the calendar cycle: what the webmail editor says, as iCalendar text. Every
/// gesture works on a copy of the loaded model and answers its serialization — the caller's object
/// is never touched — and everything the editor does not carry (X- lines, foreign alarms, the
/// overrides of a series) rides through untouched. Text is only ever produced by
/// <see cref="IcsDocument.Serialize"/>.
/// </summary>
internal static class IcsComposer
{
    private const string ProductId = "-//weesky//webmail//EN";
    private const string IcsVersion = "2.0";
    private const string Display = "DISPLAY";
    private const string Tentative = "TENTATIVE";
    private const string Cancelled = "CANCELLED";
    private const string Transparent = "TRANSPARENT";
    private const string Private = "PRIVATE";
    private const string Confidential = "CONFIDENTIAL";
    private const string DefaultReminder = "Reminder";
    private const string Mailto = "mailto:";
    internal const int MaxCommonNameLength = 100;

    private static readonly string[] Stamps = ["DTSTAMP", "LAST-MODIFIED", "SEQUENCE", "CREATED"];
    private static readonly string[] SeriesOnly = ["RRULE", "RDATE", "EXDATE", "EXRULE"];
    private static readonly TimeSpan ZoneMargin = TimeSpan.FromDays(366);

    internal static string ComposeNew(EventWrite w, string uid, DateTime nowUtc)
    {
        var calendar = Envelope();
        var evt = new CalendarEvent { Uid = uid, Created = Utc(nowUtc), Sequence = 0 };
        Apply(evt, w, withRule: true);
        Stamp(evt, nowUtc, bump: false);
        // The zone blocks are added before the event so that they precede it in the text.
        EnsureTimeZones(calendar, [evt]);
        calendar.Events.Add(evt);
        return IcsDocument.Serialize(calendar);
    }

    /// <summary>Scope All: the master takes the editor's values, keeps every line the editor does
    /// not carry, loses a CANCELLED status, and bumps SEQUENCE only when the change is one other
    /// attendees' clients have to notice.</summary>
    internal static string RewriteAll(IcsCalendar existing, EventWrite w, DateTime nowUtc)
    {
        var calendar = Clone(existing);
        var master = Master(calendar);
        var before = Shape(master);
        Apply(master, w, withRule: true);
        InviteSeries(calendar, w, master);
        Stamp(master, nowUtc, bump: Shape(master) != before);
        EnsureTimeZones(calendar, IcsDocument.Components(calendar));
        return IcsDocument.Serialize(calendar);
    }

    /// <summary>Scope This: an override at RECURRENCE-ID = <paramref name="instanceId"/>, spelled
    /// in the master's DTSTART form, replacing the one already there if any. Its SEQUENCE follows
    /// the previous override's, or the master's, and moves when the instance's timing does.</summary>
    internal static string RewriteOne(IcsCalendar existing, string instanceId, EventWrite w, DateTime nowUtc)
    {
        var calendar = Clone(existing);
        var master = Master(calendar);
        var at = InstanceAt(master, instanceId);
        var previous = IcsDocument.Components(calendar).FirstOrDefault(c => IsAt(calendar, c, at));
        var before = previous is null
            ? Timing(at, EndAt(master, at), master.Status)
            : Timing(previous.DtStart, IcsDocument.EndOf(previous), previous.Status);
        RemoveOverride(calendar, at);

        var over = Faithful(master, m => m.Copy<CalendarEvent>()!);
        foreach (var name in SeriesOnly) over.Properties.Remove(name);
        over.RecurrenceIdentifier = new RecurrenceIdentifier(at, null);
        if (previous is not null)
        {
            over.Sequence = previous.Sequence;
            TakeGuestLines(over, previous, w);
        }
        Apply(over, w, withRule: false);
        Stamp(over, nowUtc, bump: Timing(over.DtStart, IcsDocument.EndOf(over), over.Status) != before);
        calendar.Events.Add(over);
        InviteSeries(calendar, w, over);
        EnsureTimeZones(calendar, IcsDocument.Components(calendar));
        return IcsDocument.Serialize(calendar);
    }

    /// <summary>A VCALENDAR of ours holding nothing yet.</summary>
    internal static IcsCalendar Envelope() => new() { ProductId = ProductId, Version = IcsVersion };

    /// <summary>
    /// One instance as an expanded report serves it (RFC 4791 § 9.6.5): the source component as
    /// it stands — alarms, attendees, X- lines — minus the series lines, its RECURRENCE-ID and its
    /// times in UTC. A date stays a date, having no instant to convert; a floating reading is
    /// posed in the calendar's zone; an event that repeats not at all carries no RECURRENCE-ID,
    /// which would make it an override without a master to a client keying on the pair.
    /// </summary>
    internal static CalendarEvent Instance(IcsCalendar parsed, EventOccurrence occurrence,
        CalendarEvent source, string calendarTimeZone)
    {
        var instance = Faithful(source, m => m.Copy<CalendarEvent>()!);
        foreach (var name in SeriesOnly) instance.Properties.Remove(name);
        instance.Properties.Remove("DURATION");
        if (occurrence.InstanceId.Length > 0)
            instance.RecurrenceIdentifier = new RecurrenceIdentifier(
                RecurrenceIdOf(parsed, occurrence, source, calendarTimeZone), null);

        var (start, end) = Bounds(occurrence, calendarTimeZone);
        instance.DtStart = start;
        if (end is { } last) instance.DtEnd = last;
        else instance.Properties.Remove("DTEND");
        return instance;
    }

    /// <summary>An override names the slot it replaces — the RFC's own example — never the time
    /// it moved to; a generated instance names its own start.</summary>
    private static CalDateTime RecurrenceIdOf(IcsCalendar parsed, EventOccurrence occurrence,
        CalendarEvent source, string calendarTimeZone)
    {
        if (source.RecurrenceIdentifier?.StartTime is not { } slot) return Bounds(occurrence, calendarTimeZone).Start;
        return slot.HasTime
            ? Utc(IcsTimeZones.Place(slot, calendarTimeZone, parsed).Utc)
            : new CalDateTime(slot.Date);
    }

    /// <summary>The instance's own bounds in the form the report writes; no end when the source
    /// had none, an end equal to the start being a DTEND RFC 5545 § 3.8.2.2 forbids.</summary>
    private static (CalDateTime Start, CalDateTime? End) Bounds(EventOccurrence o, string calendarTimeZone)
    {
        if (o.IsAllDay) return (new CalDateTime(o.StartDate!.Value), new CalDateTime(o.EndDateExclusive!.Value));
        var (start, end) = o.IsFloating
            ? (IcsTimeZones.ToUtc(o.LocalStart!.Value, calendarTimeZone), IcsTimeZones.ToUtc(o.LocalEnd!.Value, calendarTimeZone))
            : (o.StartUtc!.Value, o.EndUtc!.Value);
        return (Utc(start), end > start ? Utc(end) : null);
    }

    internal static SplitOutcome Split(IcsCalendar existing, string instanceId, EventWrite w, string newUid, DateTime nowUtc) =>
        IcsSplitter.Split(existing, instanceId, w, newUid, nowUtc);

    internal static string RemoveOne(IcsCalendar existing, string instanceId, DateTime nowUtc)
    {
        var calendar = Clone(existing);
        var master = Master(calendar);
        var at = InstanceAt(master, instanceId);
        RemoveOverride(calendar, at);
        Rewrite(master.ExceptionDates, d => master.ExceptionDates.Add(d),
            Dates(master.ExceptionDates).Append(at).DistinctBy(d => Instant(calendar, d)).OrderBy(d => Instant(calendar, d)));
        Stamp(master, nowUtc, bump: true);
        return IcsDocument.Serialize(calendar);
    }

    /// <summary>Décision 4: the text two resources have in common once nothing but a save has
    /// happened to one of them — no stamps, and the properties in one order.</summary>
    internal static string Canonical(IcsCalendar parsed)
    {
        var copy = Clone(parsed);
        foreach (var component in Descendants(copy))
            foreach (var name in Stamps)
                component.Properties.Remove(name);
        return IcsDocument.Serialize(copy).Replace("\r\n", "\n");
    }

    internal static bool SameContent(IcsCalendar before, IcsCalendar after) => Canonical(before) == Canonical(after);

    internal static IcsCalendar Clone(IcsCalendar calendar) => Faithful(calendar, c => c.Copy<IcsCalendar>()!);

    /// <summary>Ical.Net 5.2.3's attendee copy assigns <c>Rsvp</c> on a parameter list the copy
    /// shares with its source, so RSVP=FALSE appears on both for every line that had none. The bare
    /// lines are noted before copying and the invented parameter taken back from both. The list is
    /// enumerated: its <c>ContainsKey</c> still answers true once the parameter is removed.</summary>
    private static T Faithful<T>(T source, Func<T, T> copy) where T : CalendarComponent
    {
        var bare = AttendeesIn(source).Select(a => !a.Parameters.Any(p => p.Name.Equals("RSVP", StringComparison.OrdinalIgnoreCase))).ToList();
        var result = copy(source);
        foreach (var (isBare, was, now) in bare.Zip(AttendeesIn(source), AttendeesIn(result)))
            if (isBare) { was.Parameters.Remove("RSVP"); now.Parameters.Remove("RSVP"); }
        return result;
    }

    private static IEnumerable<Attendee> AttendeesIn(CalendarComponent root) =>
        Descendants(root).Prepend(root).SelectMany(c => c.Properties.AllOf("ATTENDEE").Select(p => p.Value).OfType<Attendee>());

    /// <summary>Décision 5: a resource without a master is edited through its first component.</summary>
    internal static CalendarEvent Master(IcsCalendar calendar) =>
        IcsDocument.MasterOf(calendar) ?? IcsDocument.Components(calendar).FirstOrDefault()
        ?? throw new ArgumentException("The resource carries no VEVENT.", nameof(calendar));

    internal static CalDateTime InstanceAt(CalendarEvent master, string instanceId) =>
        IcsDocument.InstanceOf(master, instanceId)
        ?? throw new ArgumentException($"'{instanceId}' is not spelled in the form of this series' DTSTART.", nameof(instanceId));

    /// <summary>One line for every moment a resource holds, so that a date and a time compare and
    /// sort together: a dated value as its instant, a date as its UTC midnight.</summary>
    internal static DateTime Instant(IcsCalendar calendar, CalDateTime at) =>
        at.HasTime ? IcsTimeZones.Place(at, IcsTimeZones.Utc, calendar).Utc : at.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    internal static void RemoveOverride(IcsCalendar calendar, CalDateTime at)
    {
        foreach (var component in IcsDocument.Components(calendar).Where(c => IsAt(calendar, c, at)).ToList())
            Detach(calendar, component);
    }

    private static bool IsAt(IcsCalendar calendar, CalendarEvent component, CalDateTime at) =>
        component.RecurrenceIdentifier?.StartTime is { } id && id.HasTime == at.HasTime && Instant(calendar, id) == Instant(calendar, at);

    /// <summary>Where the instance at <paramref name="at"/> ends when the master alone says so.</summary>
    private static CalDateTime? EndAt(CalendarEvent master, CalDateTime at) =>
        master.DtStart is { } start && IcsDocument.EndOf(master) is { } end
            ? new CalDateTime(at.Value + (end.Value - start.Value), at.TzId, at.HasTime)
            : null;

    /// <summary>
    /// Removal by identity: the library's own Remove finds a child by Equals, which for two VEVENTs
    /// of one UID is the first one, never the one given. Nor may the flat index be used — Ical.Net
    /// 5.2.3 never chains its per-component lists, so every group starts at 0 and an index past the
    /// first group's length reads another group's item or throws. The group alone is rewritten.
    /// </summary>
    internal static void Detach(ICalendarObject parent, ICalendarObject child)
    {
        var kept = parent.Children.AllOf(child.Group).Where(sibling => !ReferenceEquals(sibling, child)).ToList();
        if (kept.Count == parent.Children.CountOf(child.Group)) return;
        parent.Children.Clear(child.Group);
        foreach (var sibling in kept) parent.Children.Add(sibling);
    }

    /// <summary>The wrapper's Remove leaves the property as it was; clearing it and adding the
    /// dates back is what takes.</summary>
    internal static void Rewrite(PeriodListWrapperBase? list, Action<CalDateTime> add, IEnumerable<CalDateTime> dates)
    {
        if (list is null) return;
        var kept = dates.ToList();
        list.Clear();
        foreach (var date in kept) add(date);
    }

    internal static IEnumerable<CalDateTime> Dates(PeriodListWrapperBase? list) => list?.GetAllDates() ?? [];

    /// <summary>The editor's values onto a component. The rule is applied to a master only — an
    /// override never repeats — and only when the editor showed it (<c>KeepRepeat</c>).</summary>
    internal static void Apply(CalendarEvent evt, EventWrite w, bool withRule)
    {
        Text(evt, "SUMMARY", w.Summary);
        Text(evt, "LOCATION", w.Location);
        Text(evt, "DESCRIPTION", w.Description);
        PlaceDates(evt, w);
        if (withRule && !w.KeepRepeat) PlaceRule(evt, w.Repeat);
        PlaceReminders(evt, w);
        PlaceAvailability(evt, w);
        PlaceUrl(evt, w.Url);
        PlaceAttendees(evt, w);
    }

    /// <summary>A series is invited whole (spec § 9): an override keeps its own dates, but the
    /// people invited to it are the people invited to the event.</summary>
    internal static void InviteSeries(IcsCalendar calendar, EventWrite w, CalendarEvent edited)
    {
        if (w.Attendees is null) return;
        foreach (var component in IcsDocument.Components(calendar).Where(c => !ReferenceEquals(c, edited)))
            PlaceAttendees(component, w);
    }

    /// <summary>An address a mailto: value carries with nothing to escape and nothing to decode: the next
    /// save must find the guest again to keep their answer, and every client must read the same text.</summary>
    internal static bool IsMailtoAddress(string email) =>
        Uri.TryCreate(Mailto + email, UriKind.Absolute, out var uri) && uri.AbsoluteUri == Mailto + email
        && IcsProjector.Address(uri) == email;

    /// <summary>An override already at that date is the reference for its own guests: what they
    /// answered to that occurrence alone, under its own ORGANIZER — never the master's lines.</summary>
    private static void TakeGuestLines(CalendarEvent over, CalendarEvent previous, EventWrite w)
    {
        over.Properties.Remove("ATTENDEE");
        foreach (var line in previous.Attendees)
        {
            if (line?.Value is not { } address) continue;
            var taken = new Attendee(address);
            CopyParameters(line.Parameters, taken.Parameters);
            over.Attendees.Add(taken);
        }
        // Once a property is removed, the library's stale index makes the next assignment of that
        // name a silent no-op: never removed when PlaceAttendees is about to write the organizer.
        if (previous.Organizer is not { Value: { } host } organizer)
        {
            if (OrganizerWritten(w) is null) over.Properties.Remove("ORGANIZER");
            return;
        }
        var kept = new Organizer(host.OriginalString);
        CopyParameters(organizer.Parameters, kept.Parameters);
        over.Organizer = kept;
    }

    /// <summary>Parameter by parameter onto a line of its own: the library's copy of a whole line
    /// shares its parameter list with the original, and invents RSVP=FALSE (see Faithful).</summary>
    private static void CopyParameters(IParameterCollection from, IParameterCollection to)
    {
        foreach (var parameter in from) to.Add(parameter.Copy<CalendarParameter>()!);
    }

    /// <summary>A CN parameter value: one line, no control character, no DQUOTE (RFC 5545 § 3.2
    /// forbids it quoted or not), at most <see cref="MaxCommonNameLength"/> characters without
    /// splitting a surrogate pair; null when nothing is left.</summary>
    internal static string? CommonName(string? raw)
    {
        var name = string.Concat((raw ?? string.Empty).ReplaceLineEndings(" ")
            .Where(c => c != '"').Select(c => char.IsControl(c) ? ' ' : c)).Trim();
        if (name.Length > MaxCommonNameLength)
            name = name[..(char.IsHighSurrogate(name[MaxCommonNameLength - 1]) ? MaxCommonNameLength - 1 : MaxCommonNameLength)].TrimEnd();
        return name.Length == 0 ? null : name;
    }

    /// <summary>Décision 8: the list exactly as sent under the ORGANIZER the caller resolved. A
    /// guest already there keeps their line — answer, role, every parameter — and only takes the
    /// name the request gives; a new one is asked (REQ-PARTICIPANT, NEEDS-ACTION, RSVP). Null
    /// leaves every line, an empty list removes the guests and the organizer with them.</summary>
    private static void PlaceAttendees(CalendarEvent evt, EventWrite w)
    {
        if (w.Attendees is null) return;
        IEnumerable<Attendee?>? stored = evt.Attendees;
        var lines = (stored ?? []).OfType<Attendee>().ToList();
        // A guest is found under the address as read today and under the escaped spelling older rows
        // hold, each decoded at most once; the decoded one wins a clash.
        var previous = new Dictionary<string, Attendee>(StringComparer.Ordinal);
        foreach (var spelling in new Func<Uri?, string?>[] { IcsProjector.Address, IcsProjector.EncodedAddress })
            foreach (var line in lines)
                if (spelling(line.Value)?.ToLowerInvariant() is { } key) previous.TryAdd(key, line);
        evt.Properties.Remove("ATTENDEE");
        foreach (var guest in w.Attendees)
        {
            var known = previous.TryGetValue(guest.Email, out var kept);
            // An address no plain mailto: value carries (accented, escaped) keeps the value the file wrote.
            var line = known && !IsMailtoAddress(guest.Email) ? new Attendee(kept!.Value!) : new Attendee(Mailto + guest.Email);
            if (known) CopyParameters(kept!.Parameters, line.Parameters);
            else (line.Role, line.ParticipationStatus, line.Rsvp) = ("REQ-PARTICIPANT", "NEEDS-ACTION", true);
            if (CommonName(guest.Name) is { } name) line.CommonName = name;
            evt.Attendees.Add(line);
        }
        if (w.Attendees.Count == 0) evt.Properties.Remove("ORGANIZER");
        else if (OrganizerWritten(w) is { } o) evt.Organizer = new Organizer(Mailto + o.Email) { CommonName = CommonName(o.Name) };
    }

    /// <summary>The ORGANIZER a write puts on the component it rewrites: only with guests, and only when resolved.</summary>
    private static OrganizerWrite? OrganizerWritten(EventWrite w) => w is { Attendees.Count: > 0, Organizer: { } o } ? o : null;

    internal static void Stamp(CalendarEvent evt, DateTime nowUtc, bool bump)
    {
        var now = Utc(nowUtc);
        evt.DtStamp = now;
        evt.LastModified = now;
        if (bump) evt.Sequence += 1;
    }

    /// <summary>What has to be the same for SEQUENCE to stay: when it starts, when it ends, how
    /// it repeats, which instances are added or removed, and its status.</summary>
    internal static string Shape(CalendarEvent evt) => string.Join("|",
        Timing(evt.DtStart, IcsDocument.EndOf(evt), evt.Status), evt.RecurrenceRule?.ToString(),
        Literals(Dates(evt.RecurrenceDates)), Literals(Dates(evt.ExceptionDates)));

    private static string Timing(CalDateTime? start, CalDateTime? end, string? status) =>
        string.Join("|", Literal(start), Literal(end), Upper(status));

    /// <summary>RFC 5545 § 3.6: a VTIMEZONE for every TZID the components cite, from a year before
    /// the earliest start. The ones the file already carries are kept as they are.</summary>
    internal static void EnsureTimeZones(IcsCalendar calendar, IEnumerable<CalendarEvent> components)
    {
        var moments = components.SelectMany(Moments).ToList();
        var present = calendar.TimeZones.Select(z => z?.TzId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var tzid in moments.Select(ZoneOf).OfType<string>().Distinct(StringComparer.Ordinal))
            if (present.Add(tzid) && IcsTimeZones.IsKnownIana(tzid)) missing.Add(tzid);
        if (missing.Count == 0) return;

        var earliest = moments.Select(at => Instant(calendar, at)).Min() - ZoneMargin;
        foreach (var tzid in missing) calendar.AddTimeZone(IcsTimeZones.Emit(tzid, earliest));
    }

    internal static CalDateTime Utc(DateTime at)
    {
        var seconds = new DateTime(at.Ticks - at.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        return new CalDateTime(seconds, IcsTimeZones.Utc, true);
    }

    private static void PlaceDates(CalendarEvent evt, EventWrite w)
    {
        evt.Properties.Remove("DURATION");
        if (w.IsAllDay)
        {
            var start = w.StartDate ?? throw new ArgumentException("An all-day event needs a start date.", nameof(w));
            var last = w.EndDateInclusive ?? start;
            if (last < start) throw new ArgumentException("The last day precedes the first.", nameof(w));
            evt.DtStart = new CalDateTime(start);
            evt.DtEnd = new CalDateTime(last.AddDays(1));
            return;
        }

        var zone = w.TimeZone is null ? null
            : IcsTimeZones.ResolveIana(w.TimeZone) ?? throw new ArgumentException($"'{w.TimeZone}' is not a time zone.", nameof(w));
        var from = w.Start ?? throw new ArgumentException("A dated event needs a start.", nameof(w));
        if (w.End < from) throw new ArgumentException("The end precedes the start.", nameof(w));
        evt.DtStart = Local(from, zone);
        if (w.End is { } to) evt.DtEnd = Local(to, zone);
        else evt.Properties.Remove("DTEND");
    }

    private static CalDateTime Local(DateTime wallClock, string? zone) =>
        new(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), zone, true);

    private static void PlaceRule(CalendarEvent evt, RecurrenceWrite? repeat)
    {
        if (repeat is null) evt.Properties.Remove("RRULE");
        else evt.RecurrenceRule = RuleOf(repeat, evt.DtStart!);
    }

    /// <summary>Shared with <see cref="IcsReader.RepeatIsExact"/>, which recomposes a stored rule
    /// through it to see whether the editor's subset says the whole of it.</summary>
    internal static RecurrencePattern RuleOf(RecurrenceWrite r, CalDateTime start)
    {
        var frequency = r.Frequency.ToUpperInvariant() switch
        {
            "DAILY" => FrequencyType.Daily,
            "WEEKLY" => FrequencyType.Weekly,
            "MONTHLY" => FrequencyType.Monthly,
            "YEARLY" => FrequencyType.Yearly,
            _ => throw new ArgumentException($"'{r.Frequency}' is not a frequency the editor offers.", nameof(r)),
        };
        var rule = new RecurrencePattern(frequency, Math.Max(1, r.Interval));
        if (r.BySetPos is { } position && r.BySetPosDay is { } day)
        {
            rule.BySetPosition = [position];
            rule.ByDay = [new WeekDay(DayOf(day))];
        }
        else if (r.ByMonthDay is { } dayOfMonth) rule.ByMonthDay = [dayOfMonth];
        else rule.ByDay = r.ByDay.Select(code => new WeekDay(DayOf(code))).ToList();

        switch (r.End)
        {
            case RecurrenceEnd.Count:
                rule.Count = r.Count is > 0 ? r.Count : throw new ArgumentException("A counted rule needs a positive count.", nameof(r));
                break;
            case RecurrenceEnd.Until:
                rule.Until = UntilOf(r.Until ?? throw new ArgumentException("A bounded rule needs its last day.", nameof(r)), start);
                break;
        }

        return rule;
    }

    /// <summary>RFC 5545 § 3.3.10: UNTIL is a DATE for an all-day rule, and an instant in UTC when
    /// DTSTART names a zone. The last day the editor shows is included whole.</summary>
    private static CalDateTime UntilOf(DateOnly until, CalDateTime start)
    {
        if (!start.HasTime) return new CalDateTime(until);
        var wallClock = until.ToDateTime(start.Time ?? TimeOnly.MinValue);
        return start.TzId is { Length: > 0 } zone
            ? Utc(IcsTimeZones.ToUtc(wallClock, zone))
            : new CalDateTime(wallClock, null, true);
    }

    private static DayOfWeek DayOf(string code) => code.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => throw new ArgumentException($"'{code}' is not a day of the week.", nameof(code)),
    };

    /// <summary>The reminders the editor shows are the DISPLAY alarms relative to the start. One
    /// already there at the same distance is kept as it is, with whatever a phone hung on it.</summary>
    private static void PlaceReminders(CalendarEvent evt, EventWrite w)
    {
        var wanted = w.ReminderMinutesBefore.Where(m => m >= 0).ToHashSet();
        foreach (var alarm in evt.Alarms.Where(IsStartReminder).ToList())
            if (!wanted.Remove(MinutesBefore(alarm))) Detach(evt, alarm);
        foreach (var minutes in wanted.Order())
            evt.Alarms.Add(new Alarm
            {
                Action = Display,
                Trigger = new Trigger(Duration.FromMinutes(-minutes)),
                Description = string.IsNullOrWhiteSpace(w.Summary) ? DefaultReminder : w.Summary.Trim(),
            });
    }

    /// <summary>The alarms the editor shows, and the only ones it may take back out — shared with
    /// <see cref="IcsReader"/> so that what is written and what is read are one rule.</summary>
    internal static bool IsStartReminder(Alarm alarm) =>
        string.Equals(alarm.Action, Display, StringComparison.OrdinalIgnoreCase)
        && alarm.Trigger is { IsRelative: true, Duration: { } span }
        && (alarm.Trigger.Related is null || alarm.Trigger.Related.Equals("START", StringComparison.OrdinalIgnoreCase))
        && span.ToTimeSpanUnspecified() <= TimeSpan.Zero;

    internal static int MinutesBefore(Alarm alarm) => (int)-alarm.Trigger!.Duration!.Value.ToTimeSpanUnspecified().TotalMinutes;

    /// <summary>Fonctionnalité 3: Tentative is a STATUS, Free a TRANSP, Private a CLASS; Busy and
    /// Default undo those and nothing else — a CONFIRMED or an explicit OPAQUE stays. Saving
    /// always lifts a CANCELLED. An all-day event is free unless the editor says busy.</summary>
    private static void PlaceAvailability(CalendarEvent evt, EventWrite w)
    {
        if (w.Availability == Availability.Tentative) evt.Status = Tentative;
        else if (Upper(evt.Status) is Tentative or Cancelled) evt.Properties.Remove("STATUS");

        if (w.Availability == Availability.Free || (w.IsAllDay && w.Availability != Availability.Busy)) evt.Transparency = Transparent;
        else if (Upper(evt.Transparency) == Transparent) evt.Properties.Remove("TRANSP");

        if (w.Visibility == Visibility.Private) evt.Class = Private;
        else if (Upper(evt.Class) is Private or Confidential) evt.Properties.Remove("CLASS");
    }

    private static void PlaceUrl(CalendarEvent evt, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) evt.Properties.Remove("URL");
        else if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) evt.Url = uri;
        else throw new ArgumentException($"'{url}' is not an absolute URL.", nameof(url));
    }

    private static void Text(CalendarEvent evt, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) evt.Properties.Remove(name);
        else evt.Properties.Set(name, value.Trim());
    }

    private static IEnumerable<CalDateTime> Moments(CalendarEvent component) =>
        new[] { component.DtStart, component.DtEnd, component.RecurrenceIdentifier?.StartTime }.OfType<CalDateTime>()
            .Concat(Dates(component.RecurrenceDates))
            .Concat(Dates(component.ExceptionDates));

    private static string? ZoneOf(CalDateTime at) =>
        at is { HasTime: true, TzId: { Length: > 0 } tzid } && tzid != IcsTimeZones.Utc ? tzid : null;

    private static IEnumerable<CalendarComponent> Descendants(ICalendarObject node)
    {
        foreach (var child in node.Children)
        {
            if (child is CalendarComponent component) yield return component;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static string Literal(CalDateTime? at) => at is null ? string.Empty : IcsDocument.LiteralOf(at) + "@" + at.TzId;

    private static string Literals(IEnumerable<CalDateTime> dates) =>
        string.Join(",", dates.Select(Literal).Order(StringComparer.Ordinal));

    private static string? Upper(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
