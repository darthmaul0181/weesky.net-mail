using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;
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

    /// <summary>Ical.Net 5.2.3 never chains its per-component lists, so every group starts at index
    /// 0: past a VTIMEZONE the flat index names another group's item, or none at all.</summary>
    [Fact]
    public void Detach_TakesTheComponentGiven_WhateverItsPositionPastTheFilesOwnZone()
    {
        var calendar = IcsDocument.TryLoad(Ics.OverrideBeforeMaster())!;
        var moved = calendar.Events.Single(e => e.RecurrenceIdentifier is not null);

        IcsComposer.Detach(calendar, moved);

        Assert.Equal("Daily", Assert.Single(IcsDocument.Components(calendar)).Summary);
        Assert.Single(calendar.TimeZones);
    }

    private static readonly OrganizerWrite Alice = new("alice@weesky.be", "Alice");

    [Fact]
    public void ComposeNew_WritesOrganizerAndEveryGuest_NeedsActionWithRsvp()
    {
        var ics = IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: Ics.Zone,
            attendees: [new("marc.dupont@example.org", "Marc Dupont"), new("julie@example.net", null)], organizer: Alice), "u1", Now);

        var master = IcsDocument.MasterOf(IcsDocument.TryLoad(ics)!)!;
        Assert.Equal("mailto:alice@weesky.be", master.Organizer!.Value!.ToString());
        Assert.Equal("Alice", master.Organizer!.CommonName);
        Assert.Collection(master.Attendees,
            a => { Assert.Equal("mailto:marc.dupont@example.org", a.Value!.ToString()); Assert.Equal("Marc Dupont", a.CommonName); Assert.Equal("NEEDS-ACTION", a.ParticipationStatus); Assert.True(a.Rsvp); Assert.Equal("REQ-PARTICIPANT", a.Role); },
            a => { Assert.Equal("mailto:julie@example.net", a.Value!.ToString()); Assert.Null(a.CommonName); });
    }

    // A name is free text the editor took from the contacts: the separators of the content line
    // itself must come back as they went in.
    [Fact]
    public void ComposeNew_AGuestNameWithLineSeparators_ReadsBackIdentical()
    {
        const string name = "Dupont: Marc; Jr, PhD";
        var ics = IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: Ics.Zone,
            attendees: [new("marc.dupont@example.org", name)], organizer: Alice with { Name = name }), "u1", Now);

        var master = IcsDocument.MasterOf(IcsDocument.TryLoad(ics)!)!;
        Assert.Equal(name, Assert.Single(master.Attendees).CommonName);
        Assert.Equal("mailto:marc.dupont@example.org", master.Attendees[0].Value!.ToString());
        Assert.Equal(name, master.Organizer!.CommonName);
    }

    [Fact]
    public void RewriteAll_KeepsAKnownGuestsAnswer_DropsTheRemoved_AndReachesOverrides()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited-override"))!;
        var ics = IcsComposer.RewriteAll(existing, Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone,
            repeat: Weekly(), attendees: [new("julie@example.net", "Julie"), new("paul@example.org", null)], organizer: Alice), Now);

        AssertTheSeriesInvites(ics, 2);
    }

    // Spec § 9: a series is invited whole — editing one occurrence cannot leave the other
    // occurrences with another guest list.
    [Fact]
    public void RewriteOne_WithAttendees_PutsTheListOnEveryComponent()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited-override"))!;
        var ics = IcsComposer.RewriteOne(existing, "20261012T100000", Write(start: Local(2026, 10, 12, 14), end: Local(2026, 10, 12, 15), tz: Ics.Zone,
            attendees: [new("julie@example.net", "Julie"), new("paul@example.org", null)], organizer: Alice), Now);

        AssertTheSeriesInvites(ics, 3);
    }

    [Fact]
    public void RewriteAll_WithNullAttendees_LeavesTheLines_AndWithEmptyRemovesThemAndTheOrganizer()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited"))!;
        var untouched = IcsComposer.RewriteAll(existing, Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone, repeat: Weekly()), Now);
        Assert.Equal(2, IcsDocument.MasterOf(IcsDocument.TryLoad(untouched)!)!.Attendees.Count);

        var emptied = IcsComposer.RewriteAll(existing, Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone, repeat: Weekly(), attendees: []), Now);
        var master = IcsDocument.MasterOf(IcsDocument.TryLoad(emptied)!)!;
        Assert.Empty(master.Attendees);
        Assert.Null(master.Organizer);
    }

    // The new series is invited whole too: an override moved past the cut carries the new list.
    [Fact]
    public void Split_WithAttendees_InvitesEveryComponentOfTheFollowingSeries()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited-override"))!;
        var invited = WriteMatching(existing) with { Attendees = [new("julie@example.net", "Julie"), new("paul@example.org", null)], Organizer = Alice };

        AssertTheSeriesInvites(IcsComposer.Split(existing, "20261012T100000", invited, "u2", Now).Following, 2);

        var emptied = IcsDocument.TryLoad(IcsComposer.Split(existing, "20261012T100000", invited with { Attendees = [], Organizer = null }, "u2", Now).Following)!;
        Assert.Equal(2, IcsDocument.Components(emptied).Count());
        Assert.All(IcsDocument.Components(emptied), c => { Assert.Empty(c.Attendees); Assert.Null(c.Organizer); });
    }

    [Fact]
    public void RewriteAll_WithEmptyAttendees_RemovesGuestsAndOrganizerFromOverridesToo()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited-override"))!;

        var emptied = IcsDocument.TryLoad(IcsComposer.RewriteAll(existing, WriteMatching(existing) with { Attendees = [] }, Now))!;

        Assert.Equal(2, IcsDocument.Components(emptied).Count());
        Assert.All(IcsDocument.Components(emptied), c => { Assert.Empty(c.Attendees); Assert.Null(c.Organizer); });
    }

    // A label or profile name is free text: a line break must never open a property of its own.
    [Fact]
    public void ComposeNew_ANameWithALineBreak_StaysInsideItsParameter()
    {
        var ics = IcsComposer.ComposeNew(Write(start: Local(2026, 9, 7, 9), end: Local(2026, 9, 7, 10), tz: Ics.Zone,
            attendees: [new("marc@example.org", "Marc\nX-EVIL:2")], organizer: Alice with { Name = "Alice\r\nX-EVIL:1" }), "u1", Now);

        var master = IcsDocument.MasterOf(IcsDocument.TryLoad(ics)!)!;
        Assert.DoesNotContain("\nX-EVIL", ics);
        Assert.Equal("Alice X-EVIL:1", master.Organizer!.CommonName);
        Assert.Equal("Marc X-EVIL:2", Assert.Single(master.Attendees).CommonName);
    }

    // Décision 8 sets ROLE, PARTSTAT and RSVP for a new guest only: a known guest's line is theirs.
    [Fact]
    public void RewriteAll_AKnownGuestKeepsEveryParameterOfTheirLine()
    {
        const string before = "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net";
        var fixture = InvitationParserTests.Fixture("webmail-invited");
        Assert.Contains(before, fixture);
        var existing = IcsDocument.TryLoad(fixture.Replace(before, "ATTENDEE;ROLE=CHAIR;CUTYPE=ROOM;X-FOO=1;PARTSTAT=ACCEPTED:mailto:Julie@Example.net"))!;
        var write = Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone, repeat: Weekly(), organizer: Alice);

        var unnamed = AttendeeLine(IcsComposer.RewriteAll(existing, write with { Attendees = [new("julie@example.net", null)] }, Now), "julie@example.net");
        Assert.All(["ROLE=CHAIR", "CUTYPE=ROOM", "X-FOO=1", "PARTSTAT=ACCEPTED"], p => Assert.Contains(p, unnamed));
        Assert.DoesNotContain("RSVP", unnamed);
        Assert.DoesNotContain("CN=", unnamed);

        var named = AttendeeLine(IcsComposer.RewriteAll(existing, write with { Attendees = [new("julie@example.net", "Julie")] }, Now), "julie@example.net");
        Assert.All(["CN=Julie", "ROLE=CHAIR", "CUTYPE=ROOM", "X-FOO=1", "PARTSTAT=ACCEPTED"], p => Assert.Contains(p, named));
        Assert.DoesNotContain("RSVP", named);
    }

    // A guest only the stored file could have written, sent back as the projector read it: the line
    // is the file's own, parameters and value, so the next save still finds them.
    [Fact]
    public void RewriteAll_AStoredGuestTheValidatorWouldRefuse_KeepsTheLineTheFileWrote()
    {
        const string julie = "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net";
        // Percent-encoded by the client that wrote it: no plain mailto: of "salle mercure@…" exists.
        const string room = "ATTENDEE;CUTYPE=ROOM;PARTSTAT=ACCEPTED:mailto:salle%20mercure@example.org";
        const string jose = "ATTENDEE;PARTSTAT=TENTATIVE:mailto:josé@example.org";
        var fixture = InvitationParserTests.Fixture("webmail-invited");
        var eol = fixture.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var existing = IcsDocument.TryLoad(fixture.Replace(julie, room + eol + jose))!;
        // What the editor sends back: every guest as the projector read it.
        var sentBack = IcsDocument.MasterOf(existing)!.Attendees.OfType<Attendee>()
            .Select(a => new AttendeeWrite(IcsProjector.Address(a.Value)!.ToLowerInvariant(), null)).ToList();

        var ics = IcsComposer.RewriteAll(existing, Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone,
            repeat: Weekly(), attendees: sentBack, organizer: Alice), Now);

        var lines = ics.Replace("\r\n ", string.Empty).Split("\r\n");
        Assert.Contains(room, lines);
        Assert.Contains(jose, lines);
    }

    // The validator and the composer read a stored guest the same way, byte for byte: an address the
    // projector decoded once (`salle%2520mercure` → `salle%20mercure`) is kept, never decoded again
    // into a `mailto:salle mercure@…` no URI can hold.
    [Fact]
    public void AStoredGuestTheValidatorLetsThrough_IsTheOneTheComposerKeeps()
    {
        const string julie = "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net";
        const string room = "ATTENDEE;CUTYPE=ROOM;PARTSTAT=ACCEPTED:mailto:salle%2520mercure@example.org";
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited").Replace(julie, room))!;
        var projected = IcsDocument.MasterOf(existing)!.Attendees.OfType<Attendee>().Select(a => IcsProjector.Address(a.Value)!).ToList();
        var request = new EventRequest
        {
            CalendarId = Guid.NewGuid(), IsAllDay = false, TimeZone = Ics.Zone,
            Start = Local(2026, 10, 5, 10), End = Local(2026, 10, 5, 11),
            Attendees = [.. projected.Select(e => new AttendeeRequest { Email = e })],
        };
        var validated = EventRequestValidator.Validate(request, projected);
        Assert.True(validated.IsSuccess, validated.IsFailure ? validated.Error : null);

        var ics = IcsComposer.RewriteAll(existing, validated.Value with { Repeat = Weekly(), Organizer = Alice }, Now);

        Assert.Contains(room, ics.Replace("\r\n ", string.Empty).Split("\r\n"));
    }

    // A calendar_attendees row projected before addresses were decoded still holds the escaped
    // spelling, and the editor sends it back: the guest is the same line under either spelling, kept
    // verbatim with its answer — never dropped and asked again.
    [Theory]
    [InlineData("mailto:jos%C3%A9@example.org", "jos%C3%A9@example.org")]
    [InlineData("mailto:jos%C3%A9@example.org", "josé@example.org")]
    [InlineData("mailto:salle%2520mercure@example.org", "salle%2520mercure@example.org")]
    public void AStoredGuestSentBackUnderEitherSpelling_KeepsItsLineAndItsAnswer(string value, string sentBack)
    {
        const string julie = "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net";
        var guest = "ATTENDEE;PARTSTAT=ACCEPTED:" + value;
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited").Replace(julie, guest))!;
        var request = new EventRequest
        {
            CalendarId = Guid.NewGuid(), IsAllDay = false, TimeZone = Ics.Zone,
            Start = Local(2026, 10, 5, 10), End = Local(2026, 10, 5, 11),
            Attendees = [new AttendeeRequest { Email = "marc.dupont@example.org" }, new AttendeeRequest { Email = sentBack }],
        };
        var validated = EventRequestValidator.Validate(request, ["marc.dupont@example.org", sentBack]);
        Assert.True(validated.IsSuccess, validated.IsFailure ? validated.Error : null);

        var ics = IcsComposer.RewriteAll(existing, validated.Value with { Repeat = Weekly(), Organizer = Alice }, Now);

        var attendees = ics.Replace("\r\n ", string.Empty).Split("\r\n").Where(l => l.StartsWith("ATTENDEE", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, attendees.Count);
        Assert.Contains(guest, attendees);
    }

    // Ical.Net's attendee copy invents RSVP=FALSE; no gesture that copies the model may carry it out.
    [Fact]
    public void Rewrites_NeverInventAnRsvp()
    {
        var existing = IcsDocument.TryLoad(InvitationParserTests.Fixture("webmail-invited"))!;
        var write = Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone, repeat: Weekly());

        Assert.DoesNotContain("RSVP=FALSE", IcsComposer.RewriteAll(existing, write, Now));
        Assert.DoesNotContain("RSVP=FALSE", IcsComposer.RewriteOne(existing, "20261012T100000", write with { Repeat = null, Start = Local(2026, 10, 12, 14), End = Local(2026, 10, 12, 15) }, Now));
        Assert.DoesNotContain("RSVP=FALSE", IcsComposer.Split(existing, "20261012T100000", write, "u2", Now).Following);
    }

    // A guest may answer one occurrence differently from the series: each component keeps its own.
    [Fact]
    public void Rewrites_KeepEachComponentsOwnAnswer()
    {
        var existing = JulieDeclinesTheOverride();
        var write = Write(start: Local(2026, 10, 5, 10), end: Local(2026, 10, 5, 11), tz: Ics.Zone,
            attendees: [new("julie@example.net", null), new("paul@example.org", null)], organizer: Alice);

        var all = IcsComposer.RewriteAll(existing, write with { Repeat = Weekly() }, Now);
        Assert.Equal([(null, "ACCEPTED"), ("20261019T100000", "DECLINED")], JuliesAnswers(all));

        var one = IcsComposer.RewriteOne(existing, "20261012T100000", write with { Start = Local(2026, 10, 12, 14), End = Local(2026, 10, 12, 15) }, Now);
        Assert.Equal([(null, "ACCEPTED"), ("20261012T100000", "ACCEPTED"), ("20261019T100000", "DECLINED")], JuliesAnswers(one));
    }

    // The override at that date is the reference for who answered what to it, never the master.
    [Fact]
    public void RewriteOne_OnAnExistingOverride_KeepsTheAnswersGivenToThatOccurrence()
    {
        var existing = JulieDeclinesTheOverride();
        var before = IcsDocument.Serialize(existing);
        var write = Write(start: Local(2026, 10, 19, 16), end: Local(2026, 10, 19, 17), tz: Ics.Zone);

        var untouched = IcsComposer.RewriteOne(existing, "20261019T100000", write, Now);
        Assert.Equal([(null, "ACCEPTED"), ("20261019T100000", "DECLINED")], JuliesAnswers(untouched));
        var moved = IcsDocument.Components(IcsDocument.TryLoad(untouched)!).Single(c => c.RecurrenceIdentifier is not null);
        Assert.Equal("Alice (this date)", moved.Organizer!.CommonName);
        Assert.Equal(2, moved.Attendees.Count);

        var invited = IcsComposer.RewriteOne(existing, "20261019T100000",
            write with { Attendees = [new("julie@example.net", null), new("paul@example.org", null)], Organizer = Alice }, Now);
        Assert.Equal([(null, "ACCEPTED"), ("20261019T100000", "DECLINED")], JuliesAnswers(invited));
        Assert.DoesNotContain("RSVP=FALSE", invited);
        Assert.All(IcsDocument.Components(IcsDocument.TryLoad(invited)!), c =>
        {
            Assert.Equal("mailto:alice@weesky.be", c.Organizer!.Value!.ToString());
            Assert.Equal("Alice", c.Organizer.CommonName);
        });
        Assert.Equal(before, IcsDocument.Serialize(existing));
    }

    // The override names no ORGANIZER of its own: the guest list still writes one on it, and the
    // empty list still takes the master's away.
    [Fact]
    public void RewriteOne_OnAnOverrideWithoutOrganizer_WritesOrRemovesItLikeEverywhereElse()
    {
        const string line = "ORGANIZER;CN=Alice:mailto:alice@weesky.be\r\n";
        var fixture = InvitationParserTests.Fixture("webmail-invited-override");
        var at = fixture.LastIndexOf(line, StringComparison.Ordinal);
        Assert.True(at > fixture.IndexOf(line, StringComparison.Ordinal));
        var existing = IcsDocument.TryLoad(fixture[..at] + fixture[(at + line.Length)..])!;
        Assert.Null(IcsDocument.Components(existing).Single(c => c.RecurrenceIdentifier is not null).Organizer);
        var write = Write(start: Local(2026, 10, 19, 16), end: Local(2026, 10, 19, 17), tz: Ics.Zone,
            attendees: [new("julie@example.net", null), new("paul@example.org", null)], organizer: Alice);

        var invited = IcsDocument.TryLoad(IcsComposer.RewriteOne(existing, "20261019T100000", write, Now))!;
        Assert.Equal(2, IcsDocument.Components(invited).Count());
        Assert.All(IcsDocument.Components(invited), c => Assert.Equal("mailto:alice@weesky.be", c.Organizer?.Value?.ToString()));

        var emptied = IcsDocument.TryLoad(IcsComposer.RewriteOne(existing, "20261019T100000", write with { Attendees = [], Organizer = null }, Now))!;
        Assert.All(IcsDocument.Components(emptied), c => { Assert.Null(c.Organizer); Assert.Empty(c.Attendees); });
    }

    /// <summary>The override fixture where Julie declined 19 October alone, under an ORGANIZER line
    /// of its own so that its origin shows.</summary>
    private static IcsCalendar JulieDeclinesTheOverride()
    {
        var fixture = InvitationParserTests.Fixture("webmail-invited-override");
        const string accepted = "PARTSTAT=ACCEPTED:mailto:Julie@Example.net";
        const string organizer = "ORGANIZER;CN=Alice:";
        var at = fixture.LastIndexOf(accepted, StringComparison.Ordinal);
        fixture = fixture[..at] + "PARTSTAT=DECLINED:mailto:Julie@Example.net" + fixture[(at + accepted.Length)..];
        at = fixture.LastIndexOf(organizer, StringComparison.Ordinal);
        return IcsDocument.TryLoad(fixture[..at] + "ORGANIZER;CN=Alice (this date):" + fixture[(at + organizer.Length)..])!;
    }

    private static IEnumerable<(string?, string?)> JuliesAnswers(string ics) =>
        IcsDocument.Components(IcsDocument.TryLoad(ics)!)
            .Select(c => (IcsDocument.InstanceIdOf(c) is { Length: > 0 } id ? id : null,
                c.Attendees.Single(a => a.Value!.ToString().EndsWith("julie@example.net", StringComparison.OrdinalIgnoreCase)).ParticipationStatus))
            .OrderBy(p => p.Item1, StringComparer.Ordinal);

    private static string AttendeeLine(string ics, string address) =>
        ics.Replace("\r\n ", string.Empty).Split("\r\n").Single(l => l.StartsWith("ATTENDEE", StringComparison.Ordinal) && l.EndsWith(":mailto:" + address, StringComparison.Ordinal));

    private static void AssertTheSeriesInvites(string ics, int components)
    {
        var parsed = IcsDocument.Components(IcsDocument.TryLoad(ics)!).ToList();
        Assert.Equal(components, parsed.Count);
        foreach (var component in parsed)
        {
            Assert.Equal("mailto:alice@weesky.be", component.Organizer!.Value!.ToString());
            Assert.Equal(["julie@example.net", "paul@example.org"], component.Attendees.Select(a => a.Value!.ToString()["mailto:".Length..]).Order());
            Assert.Equal("ACCEPTED", component.Attendees.Single(a => a.Value!.ToString().EndsWith("julie@example.net", StringComparison.Ordinal)).ParticipationStatus);
            Assert.Equal("NEEDS-ACTION", component.Attendees.Single(a => a.Value!.ToString().EndsWith("paul@example.org", StringComparison.Ordinal)).ParticipationStatus);
        }
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
        Availability availability = Availability.Busy, Visibility visibility = Visibility.Default, string? url = null,
        IReadOnlyList<AttendeeWrite>? attendees = null, OrganizerWrite? organizer = null) =>
        new(Guid.Empty, "Standup", null, null, allDay is not null, start, end, tz,
            allDay?.Start, allDay?.EndInclusive, repeat, reminders ?? [], availability, visibility, url,
            Attendees: attendees, Organizer: organizer);

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
