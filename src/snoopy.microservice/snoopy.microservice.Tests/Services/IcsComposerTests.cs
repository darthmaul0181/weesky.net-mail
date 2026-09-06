using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IcsComposerTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_Dated_WritesTzidAndVtimezone_NeverUtc()
    {
        var text = IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: Ics.Zone, repeat: Weekly()), "u1", Now);

        Assert.Contains("PRODID:-//weesky//webmail//EN", text);
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20260907T090000", text);
        Assert.Contains("BEGIN:VTIMEZONE", text);
        Assert.Contains("BEGIN:DAYLIGHT", text);
        Assert.Contains("RRULE:FREQ=WEEKLY", text);
        Assert.Contains("DTSTAMP:20260904T100000Z", text);
        Assert.Contains("CREATED:20260904T100000Z", text);
        Assert.Contains("SEQUENCE:0", text);
        Assert.Contains("UID:u1", text);
        Assert.DoesNotContain("DTSTART:20260907T070000Z", text);
        Assert.Equal([7, 14, 21, 28], Expand(text, to: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).Select(o => o.StartUtc!.Value.Day));
    }

    [Fact]
    public void New_AllDay_WritesDateAndExclusiveEnd_Transparent()
    {
        var text = IcsComposer.ComposeNew(Write(allDay: (new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 17)), availability: Availability.Free), "u1", Now);

        Assert.Contains("DTSTART;VALUE=DATE:20260915", text);
        Assert.Contains("DTEND;VALUE=DATE:20260918", text);
        Assert.Contains("TRANSP:TRANSPARENT", text);
        Assert.DoesNotContain("VTIMEZONE", text);
        var occurrence = Assert.Single(Expand(text));
        Assert.Equal(new DateOnly(2026, 9, 15), occurrence.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 18), occurrence.EndDateExclusive);
    }

    [Fact]
    public void New_AllDay_IsFreeUnlessBusy()
    {
        Assert.Contains("TRANSP:TRANSPARENT", IcsComposer.ComposeNew(Write(allDay: (new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15)), availability: Availability.Tentative), "u1", Now));
        Assert.DoesNotContain("TRANSP", IcsComposer.ComposeNew(Write(allDay: (new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15)), availability: Availability.Busy), "u1", Now));
    }

    [Fact]
    public void Availability_MapsToStatusAndTransp()
    {
        Assert.Contains("STATUS:TENTATIVE", Compose(availability: Availability.Tentative));
        var busy = Compose(availability: Availability.Busy);
        Assert.DoesNotContain("STATUS:", busy);
        Assert.DoesNotContain("TRANSP:TRANSPARENT", busy);
        Assert.DoesNotContain("CLASS:", busy);
        Assert.Contains("CLASS:PRIVATE", Compose(visibility: Visibility.Private));
        Assert.Contains("URL:https://weesky.be/", Compose(url: "https://weesky.be/"));
    }

    [Fact]
    public void Invalid_EndBeforeStart_UnknownZone_ForeignFrequency_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 10), end: Local(2026, 9, 7, 9), tz: Ics.Zone), "u1", Now));
        Assert.Throws<ArgumentException>(() => IcsComposer.ComposeNew(Write(allDay: (new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 6))), "u1", Now));
        Assert.Throws<ArgumentException>(() => IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: "Nowhere/Land"), "u1", Now));
        Assert.Throws<ArgumentException>(() => Compose(repeat: Weekly() with { Frequency = "HOURLY" }));
        Assert.Throws<ArgumentException>(() => Compose(repeat: Weekly() with { End = RecurrenceEnd.Count, Count = 0 }));
        Assert.Throws<ArgumentException>(() => Compose(repeat: Weekly() with { End = RecurrenceEnd.Count, Count = null }));
        Assert.Throws<ArgumentException>(() => Compose(repeat: Weekly() with { End = RecurrenceEnd.Until, Until = null }));
        Assert.Throws<ArgumentException>(() => IcsComposer.RemoveOne(IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY"))!, "20260914", Now));
    }

    [Fact]
    public void Reminder_WritesDisplayAlarmRelativeToStart()
    {
        Assert.Contains("TRIGGER:-PT15M", Compose(reminders: [15]));

        var alarms = IcsDocument.MasterOf(IcsDocument.TryLoad(Compose(reminders: [0, 60]))!)!.Alarms.ToList();
        Assert.Equal(2, alarms.Count);
        Assert.All(alarms, a => Assert.Equal("DISPLAY", a.Action));
        Assert.All(alarms, a => Assert.True(a.Trigger!.IsRelative));
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMinutes(-60)], alarms.Select(a => a.Trigger!.Duration!.Value.ToTimeSpanUnspecified()));
    }

    [Fact]
    public void Rule_UntilIsUtcWhenZoned_DateWhenAllDay_CountXorUntil()
    {
        var until = Weekly() with { End = RecurrenceEnd.Until, Until = new DateOnly(2026, 10, 5) };
        Assert.Contains("RRULE:FREQ=WEEKLY;UNTIL=20261005T070000Z;BYDAY=MO", Compose(repeat: until));
        Assert.Contains("UNTIL=20261005;", IcsComposer.ComposeNew(Write(allDay: (new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7)), repeat: until), "u1", Now));

        var counted = Compose(repeat: Weekly() with { End = RecurrenceEnd.Count, Count = 5, Until = new DateOnly(2026, 10, 5) });
        Assert.Contains("COUNT=5", counted);
        Assert.DoesNotContain("UNTIL", counted);
        Assert.Equal(5, Expand(counted).Count);
    }

    [Fact]
    public void Rule_Monthly_SecondTuesday_And_Every15th()
    {
        var second = new RecurrenceWrite("MONTHLY", 2, [], null, 2, "TU", RecurrenceEnd.Never, null, null);
        var rule = IcsDocument.MasterOf(IcsDocument.TryLoad(Compose(repeat: second))!)!.RecurrenceRule!;
        Assert.Equal([2], rule.BySetPosition);
        Assert.Equal(DayOfWeek.Tuesday, Assert.Single(rule.ByDay).DayOfWeek);
        Assert.Equal(2, rule.Interval);

        Assert.Contains("RRULE:FREQ=MONTHLY;BYMONTHDAY=15", Compose(repeat: new RecurrenceWrite("MONTHLY", 1, [], 15, null, null, RecurrenceEnd.Never, null, null)));
    }

    [Fact]
    public void RewriteAll_KeepsForeignLines_AndBumpsSequenceOnlyOnSignificantChange()
    {
        var existing = IcsDocument.TryLoad(Ics.FromPhone())!;
        var same = IcsComposer.RewriteAll(existing, WriteMatching(existing), Now);
        Assert.True(IcsComposer.SameContent(existing, IcsDocument.TryLoad(same)!));

        var retitled = IcsDocument.TryLoad(IcsComposer.RewriteAll(existing, WriteMatching(existing) with { Summary = "Renamed" }, Now))!;
        Assert.Contains("X-APPLE-TRAVEL-ADVISORY-BEHAVIOR", IcsDocument.Serialize(retitled));
        Assert.Contains("ACTION:EMAIL", IcsDocument.Serialize(retitled));
        Assert.Contains("X-WR-ALARMUID:0A1B2C3D", IcsDocument.Serialize(retitled));
        Assert.Equal("Renamed", IcsDocument.MasterOf(retitled)!.Summary);
        Assert.Equal("Standup (moved)", retitled.Events.Single(e => e.RecurrenceIdentifier is not null).Summary);
        Assert.Equal(IcsDocument.MasterOf(existing)!.Sequence, IcsDocument.MasterOf(retitled)!.Sequence);

        var moved = IcsDocument.TryLoad(IcsComposer.RewriteAll(existing, WriteMatching(existing) with { Start = Local(2026, 9, 7, 10) }, Now))!;
        Assert.Equal(IcsDocument.MasterOf(existing)!.Sequence + 1, IcsDocument.MasterOf(moved)!.Sequence);
        Assert.Equal(Now, IcsDocument.MasterOf(moved)!.LastModified!.AsUtc);
        Assert.Equal(Now, IcsDocument.MasterOf(moved)!.DtStamp!.AsUtc);
        Assert.Equal(8, Expand(IcsDocument.Serialize(moved)).First().StartUtc!.Value.Hour);
    }

    [Fact]
    public void RewriteAll_ReplacesDisplayReminders_KeepsTheOthers_AndDropsCancelled()
    {
        var existing = IcsDocument.TryLoad(Ics.FromPhone())!;
        var text = IcsComposer.RewriteAll(existing, WriteMatching(existing) with { ReminderMinutesBefore = [30] }, Now);
        Assert.DoesNotContain("TRIGGER:-PT15M", text);
        Assert.Contains("TRIGGER:-PT30M", text);
        Assert.Contains("TRIGGER;RELATED=END:-PT5M", text);
        Assert.Contains("ACTION:EMAIL", text);
        Assert.Equal(2, IcsDocument.MasterOf(IcsDocument.TryLoad(text)!)!.Alarms.Count);

        var cancelled = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY", extra: "STATUS:CANCELLED"))!;
        Assert.DoesNotContain("STATUS", IcsComposer.RewriteAll(cancelled, WriteMatching(cancelled), Now));
    }

    [Fact]
    public void RewriteAll_WithoutSequence_WritesOneOnSignificantChange()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY"))!;
        Assert.DoesNotContain("SEQUENCE", IcsComposer.RewriteAll(existing, WriteMatching(existing) with { Summary = "Renamed" }, Now));
        Assert.Contains("SEQUENCE:1", IcsComposer.RewriteAll(existing, WriteMatching(existing) with { Repeat = Weekly("TU") }, Now));
    }

    [Fact]
    public void RewriteOne_WritesRecurrenceIdInMasterForm()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY"))!;
        var text = IcsComposer.RewriteOne(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 11), End = Local(2026, 9, 14, 12) }, Now);

        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20260914T090000", text);
        Assert.Contains("BEGIN:VTIMEZONE", text);
        var occurrences = OccurrenceExpander.Expand(Guid.Empty, Guid.Empty, IcsDocument.TryLoad(text)!, new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), Ics.Zone, Ics.Zone);
        Assert.Equal(9, Assert.Single(occurrences).StartUtc!.Value.Hour);
        Assert.Equal(4, Expand(text, to: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).Count);
    }

    [Fact]
    public void RewriteOne_BumpsTheOverridesSequence_OnlyWhenItsTimingMoves()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY"))!;
        var untouched = IcsComposer.RewriteOne(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 9), End = Local(2026, 9, 14, 10), Summary = "Once" }, Now);
        Assert.Equal(0, Override(untouched).Sequence);

        var moved = IcsComposer.RewriteOne(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 11), End = Local(2026, 9, 14, 12) }, Now);
        Assert.Equal(1, Override(moved).Sequence);
        var retitled = IcsComposer.RewriteOne(IcsDocument.TryLoad(moved)!, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 11), End = Local(2026, 9, 14, 12), Summary = "Renamed" }, Now);
        Assert.Equal(1, Override(retitled).Sequence);
        Assert.Equal("Renamed", Override(retitled).Summary);
        var movedAgain = IcsComposer.RewriteOne(IcsDocument.TryLoad(retitled)!, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 12), End = Local(2026, 9, 14, 13) }, Now);
        Assert.Equal(2, Override(movedAgain).Sequence);
        Assert.Equal(0, IcsDocument.MasterOf(IcsDocument.TryLoad(movedAgain)!)!.Sequence);
    }

    [Fact]
    public void RewriteOne_OnAllDay_WritesDateForm() =>
        Assert.Contains("RECURRENCE-ID;VALUE=DATE:20260914", IcsComposer.RewriteOne(IcsDocument.TryLoad(Ics.AllDayWeekly())!, "20260914", WriteAllDay(new DateOnly(2026, 9, 15)), Now));

    [Fact]
    public void RewriteOne_ReplacesTheExistingOverride_AndWritesNoRule()
    {
        var existing = IcsDocument.TryLoad(Ics.WeeklyWithExdateAndOverride())!;
        var text = IcsComposer.RewriteOne(existing, "20260914T090000", WriteMatching(existing) with { Summary = "Once" }, Now);

        var reloaded = IcsDocument.TryLoad(text)!;
        var over = Assert.Single(reloaded.Events, e => e.RecurrenceIdentifier is not null);
        Assert.Equal("Once", over.Summary);
        Assert.Null(over.RecurrenceRule);
        Assert.Equal(Now, over.LastModified!.AsUtc);
        Assert.Equal(2, reloaded.Events.Count);
    }

    [Fact]
    public void RemoveOne_AddsExdate_AndDropsTheOverride()
    {
        var text = IcsComposer.RemoveOne(IcsDocument.TryLoad(Ics.WeeklyWithExdateAndOverride())!, "20260914T090000", Now);

        Assert.Contains("EXDATE;TZID=Europe/Brussels:20260914T090000", text);
        var master = IcsDocument.MasterOf(IcsDocument.TryLoad(text)!)!;
        Assert.Equal([14, 21], master.ExceptionDates.GetAllDates().Select(d => d.Day).Order());
        Assert.Single(IcsDocument.TryLoad(text)!.Events);
        Assert.Contains("SEQUENCE:1", text);
        Assert.Equal([7, 28], Expand(text, to: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).Select(o => o.StartUtc!.Value.Day));
    }

    [Fact]
    public void Split_UntilIsTheInstantBefore_InUtc_CountIsCarried()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY;COUNT=10"))!;
        var outcome = IcsComposer.Split(existing, "20260928T090000", WriteMatching(existing), "u2", Now);

        Assert.Contains("RRULE:FREQ=WEEKLY;UNTIL=20260928T065959Z", outcome.Original);
        Assert.DoesNotContain("COUNT", outcome.Original);
        Assert.Contains("RRULE:FREQ=WEEKLY;COUNT=7", outcome.Following);
        Assert.Contains("UID:u2", outcome.Following);
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20260928T090000", outcome.Following);
        Assert.Contains("DTEND;TZID=Europe/Brussels:20260928T100000", outcome.Following);
        Assert.False(outcome.DroppedExceptions);
        Assert.Equal([7, 14, 21], Expand(outcome.Original).Select(o => o.StartUtc!.Value.Day));
        Assert.Equal(7, Expand(outcome.Following, to: new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Count);
    }

    [Fact]
    public void Split_KeepsAnEarlierStartTheUserChose()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY;COUNT=10"))!;
        var outcome = IcsComposer.Split(existing, "20260928T090000", WriteMatching(existing) with { Start = Local(2026, 9, 27, 8), End = Local(2026, 9, 27, 9) }, "u2", Now);

        Assert.Contains("DTSTART;TZID=Europe/Brussels:20260927T080000", outcome.Following);
        Assert.Contains("UNTIL=20260928T065959Z", outcome.Original);
        Assert.Equal([7, 14, 21], Expand(outcome.Original).Select(o => o.StartUtc!.Value.Day));
        Assert.Equal(27, Expand(outcome.Following).First().StartUtc!.Value.Day);
    }

    [Fact]
    public void Split_RdatesFollowTheExdates_RebasedOrDroppedTogether()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY", extra: "EXDATE;TZID=Europe/Brussels:20260921T090000\r\nRDATE;TZID=Europe/Brussels:20260923T090000"))!;

        var timeOnly = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 10), End = Local(2026, 9, 14, 11) }, "u2", Now);
        var next = IcsDocument.MasterOf(IcsDocument.TryLoad(timeOnly.Following)!)!;
        Assert.Equal("20260921T100000", IcsDocument.LiteralOf(Assert.Single(next.ExceptionDates.GetAllDates())));
        Assert.Equal("20260923T100000", IcsDocument.LiteralOf(Assert.Single(next.RecurrenceDates.GetAllDates())));
        Assert.False(timeOnly.DroppedExceptions);
        Assert.Equal([(14, 8), (23, 8), (28, 8)], Expand(timeOnly.Following, to: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).Select(o => (o.StartUtc!.Value.Day, o.StartUtc!.Value.Hour)));
        Assert.DoesNotContain("RDATE", timeOnly.Original);

        var dayChange = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 15, 9), End = Local(2026, 9, 15, 10), Repeat = Weekly("TU") }, "u2", Now);
        Assert.True(dayChange.DroppedExceptions);
        Assert.DoesNotContain("RDATE", dayChange.Following);
        Assert.DoesNotContain("EXDATE", dayChange.Following);

        Assert.True(IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Repeat = null }, "u2", Now).DroppedExceptions);
    }

    [Fact]
    public void Split_AllDay_UntilIsTheDayBefore()
    {
        var outcome = IcsComposer.Split(IcsDocument.TryLoad(Ics.AllDayWeekly())!, "20260914", WriteAllDay(new DateOnly(2026, 9, 14)) with { Repeat = Weekly() }, "u2", Now);

        Assert.Contains("UNTIL=20260913", outcome.Original);
        Assert.Contains("DTSTART;VALUE=DATE:20260914", outcome.Following);
        Assert.Equal([new DateOnly(2026, 9, 7)], Expand(outcome.Original).Select(o => o.StartDate!.Value));
    }

    [Fact]
    public void Split_MovesLaterExceptions_RebasesTimeOnlyChange_DropsDayChange()
    {
        var existing = IcsDocument.TryLoad(Ics.WeeklyWithExdateAndOverride())!;

        var timeOnly = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 14, 10), End = Local(2026, 9, 14, 11) }, "u2", Now);
        Assert.Contains("EXDATE;TZID=Europe/Brussels:20260921T100000", timeOnly.Following);
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20260914T100000", timeOnly.Following);
        Assert.DoesNotContain("EXDATE", timeOnly.Original);
        Assert.Single(IcsDocument.TryLoad(timeOnly.Original)!.Events);
        Assert.False(timeOnly.DroppedExceptions);
        Assert.All(IcsDocument.TryLoad(timeOnly.Following)!.Events, e => Assert.Equal("u2", e.Uid));
        Assert.Equal([7], Expand(timeOnly.Original).Select(o => o.StartUtc!.Value.Day));
        Assert.Equal([(14, 9), (28, 8)], Expand(timeOnly.Following, to: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).Select(o => (o.StartUtc!.Value.Day, o.StartUtc!.Value.Hour)));

        var dayChange = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 15, 9), End = Local(2026, 9, 15, 10), Repeat = Weekly("TU") }, "u2", Now);
        Assert.True(dayChange.DroppedExceptions);
        Assert.Single(IcsDocument.TryLoad(dayChange.Following)!.Events);
        Assert.Contains("BYDAY=TU", dayChange.Following);
        Assert.DoesNotContain("EXDATE", dayChange.Following);
    }

    [Fact]
    public void Split_Weekly_RecalculatesByDayWhenTheDayMoves()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY;BYDAY=MO"))!;
        var outcome = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Start = Local(2026, 9, 16, 9), End = Local(2026, 9, 16, 10) }, "u2", Now);

        Assert.Contains("BYDAY=WE", outcome.Following);
        Assert.DoesNotContain("BYDAY=MO", outcome.Following);
        Assert.Equal([16, 23, 30], Expand(outcome.Following, to: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).Select(o => o.StartUtc!.Value.Day));
    }

    [Fact]
    public void Split_StopRepeating_FollowingIsSingle()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY"))!;
        var outcome = IcsComposer.Split(existing, "20260914T090000", WriteMatching(existing) with { Repeat = null }, "u2", Now);

        Assert.Null(IcsDocument.MasterOf(IcsDocument.TryLoad(outcome.Following)!)!.RecurrenceRule);
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20260914T090000", outcome.Following);
        Assert.Contains("UNTIL=20260914T065959Z", outcome.Original);
        var single = Assert.Single(Expand(outcome.Following));
        Assert.Equal(string.Empty, single.InstanceId);
    }

    [Fact]
    public void Split_UnwalkableSeries_KeepsTheEditorsCount()
    {
        var existing = IcsDocument.TryLoad(Ics.Rule("FREQ=HOURLY;COUNT=10"))!;
        var daily = new RecurrenceWrite("DAILY", 1, [], null, null, null, RecurrenceEnd.Count, 10, null);
        var outcome = IcsComposer.Split(existing, "20260907T120000", WriteMatching(existing) with { Repeat = daily }, "u2", Now);

        Assert.Contains("UNTIL=20260907T095959Z", outcome.Original);
        Assert.Contains("RRULE:FREQ=DAILY;COUNT=10", outcome.Following);
        Assert.DoesNotContain("UNTIL", outcome.Following);
        Assert.Equal(10, Expand(outcome.Following).Count);
        Assert.Contains("DTSTART;TZID=Europe/Brussels:20260907T120000", outcome.Following);
    }

    [Fact]
    public void Canonical_IgnoresStampsAndFormatting()
    {
        var a = IcsDocument.TryLoad(Ics.FromPhone())!;
        var b = IcsDocument.TryLoad(Ics.FromPhone().Replace("DTSTAMP:20260901T080000Z", "DTSTAMP:20260904T100000Z").Replace("\r\n", "\n"))!;
        Assert.True(IcsComposer.SameContent(a, b));

        var reordered = IcsDocument.TryLoad(Ics.FromPhone().Replace("SUMMARY:Standup\r\nLOCATION:Room 4\r\n", "LOCATION:Room 4\r\nSUMMARY:Standup\r\n"))!;
        Assert.True(IcsComposer.SameContent(a, reordered));
        Assert.False(IcsComposer.SameContent(a, IcsDocument.TryLoad(Ics.FromPhone().Replace("LOCATION:Room 4", "LOCATION:Room 5"))!));
        Assert.Equal("Standup", IcsDocument.MasterOf(a)!.Summary);
        Assert.Contains("DTSTAMP", IcsDocument.Serialize(a));
    }

    /// <summary>A rule the editor cannot state stays exactly as the file spells it while the rest
    /// of the event takes the editor's values.</summary>
    [Fact]
    public void KeepRepeat_LeavesARichRuleUntouched()
    {
        var parsed = IcsDocument.TryLoad(Ics.Rule("FREQ=YEARLY;BYMONTH=3,9;BYDAY=-1MO"))!;
        var master = IcsDocument.MasterOf(parsed)!;
        var write = IcsReader.Read(parsed, Guid.NewGuid()) with { KeepRepeat = true, Summary = "Renamed" };

        IcsComposer.Apply(master, write, withRule: true);

        var rule = master.RecurrenceRule!;
        Assert.Equal(FrequencyType.Yearly, rule.Frequency);
        Assert.Equal([3, 9], rule.ByMonth);
        Assert.Equal(-1, Assert.Single(rule.ByDay).Offset);
        Assert.Equal(DayOfWeek.Monday, rule.ByDay[0].DayOfWeek);
        Assert.Equal("Renamed", master.Summary);
    }

    private static CalendarEvent Override(string ics) =>
        IcsDocument.TryLoad(ics)!.Events.Single(e => e.RecurrenceIdentifier is not null);

    private static IReadOnlyList<EventOccurrence> Expand(string ics, DateTime? to = null) =>
        OccurrenceExpander.Expand(Guid.Empty, Guid.Empty, IcsDocument.TryLoad(ics)!, From, to ?? To, Ics.Zone, Ics.Zone);

    private static DateTime Local(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);

    private static RecurrenceWrite Weekly(string byDay = "MO") =>
        new("WEEKLY", 1, [byDay], null, null, null, RecurrenceEnd.Never, null, null);

    private static string Compose(
        RecurrenceWrite? repeat = null, IReadOnlyList<int>? reminders = null,
        Availability availability = Availability.Busy, Visibility visibility = Visibility.Default, string? url = null) =>
        IcsComposer.ComposeNew(
            Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: Ics.Zone, repeat: repeat,
                  reminders: reminders, availability: availability, visibility: visibility, url: url),
            "u1", Now);

    private static EventWrite Write(
        DateTime? start = null, DateTime? end = null, string? tz = null, RecurrenceWrite? repeat = null,
        (DateOnly Start, DateOnly EndInclusive)? allDay = null, IReadOnlyList<int>? reminders = null,
        Availability availability = Availability.Busy, Visibility visibility = Visibility.Default, string? url = null) =>
        new(Guid.Empty, "Standup", null, null, allDay is not null, start, end, tz,
            allDay?.Start, allDay?.EndInclusive, repeat, reminders ?? [], availability, visibility, url);

    private static EventWrite WriteAllDay(DateOnly day) => Write(allDay: (day, day));

    /// <summary>The master read back as the editor would send it unchanged.</summary>
    private static EventWrite WriteMatching(IcsCalendar calendar)
    {
        var master = IcsDocument.MasterOf(calendar)!;
        var start = master.DtStart!;
        var end = IcsDocument.EndOf(master) ?? start;
        var allDay = !start.HasTime;
        return new EventWrite(
            Guid.Empty, master.Summary, master.Location, master.Description, allDay,
            allDay ? null : start.Value, allDay ? null : end.Value, allDay ? null : start.TzId,
            allDay ? start.Date : null, allDay ? end.Date.AddDays(-1) : null,
            RepeatOf(master.RecurrenceRule, start),
            master.Alarms.Where(IsStartReminder).Select(a => (int)-a.Trigger!.Duration!.Value.ToTimeSpanUnspecified().TotalMinutes).ToList(),
            master.Status == "TENTATIVE" ? Availability.Tentative : master.Transparency == "TRANSPARENT" ? Availability.Free : Availability.Busy,
            master.Class == "PRIVATE" ? Visibility.Private : Visibility.Default,
            master.Url?.ToString());
    }

    private static bool IsStartReminder(Alarm alarm) =>
        alarm.Action == "DISPLAY" && alarm.Trigger is { IsRelative: true, Related: null or "START", Duration: not null };

    private static RecurrenceWrite? RepeatOf(RecurrenceRule? rule, CalDateTime start)
    {
        if (rule is null) return null;
        var positioned = rule.BySetPosition.Count > 0 ? rule.ByDay.FirstOrDefault() : rule.ByDay.FirstOrDefault(d => d.Offset is not null);
        var until = rule.Until is { } u
            ? DateOnly.FromDateTime(u.IsUtc && start.TzId is { } tz ? IcsTimeZones.FromUtc(u.AsUtc, tz) : u.Value)
            : (DateOnly?)null;
        return new RecurrenceWrite(
            rule.Frequency.ToString().ToUpperInvariant(), rule.Interval,
            positioned is null ? rule.ByDay.Select(Code).ToList() : [],
            rule.ByMonthDay.Count > 0 ? rule.ByMonthDay[0] : null,
            positioned is null ? null : rule.BySetPosition.Count > 0 ? rule.BySetPosition[0] : positioned.Offset,
            positioned is null ? null : Code(positioned),
            rule.Count is > 0 ? RecurrenceEnd.Count : until is null ? RecurrenceEnd.Never : RecurrenceEnd.Until,
            rule.Count is > 0 ? rule.Count : null, until);
    }

    private static string Code(WeekDay day) => day.DayOfWeek.ToString()[..2].ToUpperInvariant();
}
