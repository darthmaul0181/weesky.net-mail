using System.Globalization;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class OccurrenceExpanderTests
{
    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Weekly_WithExdateAndOverride()
    {
        var list = Expand(Ics.WeeklyWithExdateAndOverride());

        Assert.Equal([7, 14, 28], list.Select(o => o.StartUtc!.Value.Day));
        var moved = list.Single(o => o.IsOverride);
        Assert.Equal("20260914T090000", moved.InstanceId);   // l'identifiant est l'origine, pas l'heure déplacée
        Assert.Equal(9, moved.StartUtc!.Value.Hour);          // 11:00 Bruxelles = 09:00 UTC
        Assert.All(list.Where(o => !o.IsOverride), o => Assert.Equal("Europe/Brussels", o.TimeZone));
        Assert.Equal(["20260907T090000", "20260914T090000", "20260928T090000"], list.Select(o => o.InstanceId));
    }

    [Fact]
    public void Single_HasEmptyInstanceId()
    {
        var o = Expand(Ics.Single(start: "DTSTART:20260907T090000Z", end: null)).Single();

        Assert.Equal(string.Empty, o.InstanceId);
        Assert.False(o.IsOverride);
        Assert.Equal("UTC", o.TimeZone);
        Assert.Null(o.RecurrenceText);
        Assert.Equal(DateTimeKind.Utc, o.StartUtc!.Value.Kind);
    }

    [Fact]
    public void AllDay_KeepsDates_EndExclusive_AndFollowsViewZoneAtTheEdge()
    {
        var ics = Ics.Single(start: "DTSTART;VALUE=DATE:20260930", end: "DTEND;VALUE=DATE:20261001");

        var brussels = Expand(ics, From, To, view: "Europe/Brussels").Single();
        Assert.True(brussels.IsAllDay);
        Assert.Null(brussels.StartUtc);
        Assert.Equal(new DateOnly(2026, 9, 30), brussels.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 1), brussels.EndDateExclusive);

        // Fenêtre [1er sept 00:00 UTC, 1er oct 00:00 UTC[ vue depuis Los Angeles : le 30 sept y est encore.
        Assert.Single(Expand(ics, From, To, view: "America/Los_Angeles"));
        // A window closing at 30 Sep 00:00Z already sees two hours of the 30th from Brussels, where
        // the day opens at 22:00Z on the 29th — and nothing of it from UTC.
        var edge = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
        Assert.Single(Expand(ics, From, edge, view: "Europe/Brussels"));
        Assert.Empty(Expand(ics, From, edge, view: IcsTimeZones.Utc));
    }

    [Fact]
    public void Floating_IsReturnedAsLocal_AndCutInViewZone()
    {
        var o = Expand(Ics.Single(start: "DTSTART:20260907T090000", end: "DTEND:20260907T100000")).Single();

        Assert.True(o.IsFloating);
        Assert.Null(o.StartUtc);
        Assert.Null(o.TimeZone);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0), o.LocalStart);
        Assert.Equal(new DateTime(2026, 9, 7, 10, 0, 0), o.LocalEnd);
        Assert.Equal(DateTimeKind.Unspecified, o.LocalStart!.Value.Kind);
    }

    /// <summary>
    /// Le 14 septembre 09:00 Bruxelles est 07:00 UTC : la fenêtre est prise à l'intérieur de cette
    /// heure-là, et la suivante, juste après sa fin, ne ramène rien.
    /// </summary>
    [Fact]
    public void RecurringMatchesWhenOneOccurrenceTouchesTheWindow()
    {
        Assert.Single(Expand(Ics.Rule("FREQ=WEEKLY"),
            new DateTime(2026, 9, 14, 7, 30, 0, DateTimeKind.Utc), new DateTime(2026, 9, 14, 7, 45, 0, DateTimeKind.Utc)));
        Assert.Empty(Expand(Ics.Rule("FREQ=WEEKLY"),
            new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 14, 8, 30, 0, DateTimeKind.Utc)));
    }

    /// <summary>
    /// Un TZID qu'aucune base ne résout : Ical.Net lève pendant l'énumération, donc la série est
    /// parcourue en flottant et chaque instant reposé. Sans bloc VTIMEZONE dans le fichier, le
    /// troisième palier n'a rien à lire et l'heure murale est posée dans le fuseau de l'agenda.
    /// </summary>
    [Fact]
    public void UnresolvableZone_IsStillExpanded_AndPlacedInTheCalendarZone()
    {
        var list = Expand(Ics.Single(start: "DTSTART;TZID=Custom/Nowhere:20260907T090000", end: null,
            extra: "RRULE:FREQ=WEEKLY;COUNT=2"));

        Assert.Equal(2, list.Count);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), list[0].StartUtc);
        Assert.Equal("20260907T090000", list[0].InstanceId);
        Assert.Null(list[0].TimeZone);
    }

    /// <summary>
    /// Le palier 3 du corpus réel : le `VTIMEZONE` du fichier est la seule chose qui sait que
    /// « Canberra, Melbourne, Sydney » vaut +1100 le 10 mars 2009. Miroir de
    /// <see cref="IcsCorpusTests.Outlook2003_ReadsItsOwnVtimezone"/>, côté expansion : sans le
    /// parcours détaché, Ical.Net lève et l'événement est invisible sur la grille.
    /// </summary>
    [Fact]
    public void Outlook2003_TierThreeZone_IsOnTheGrid()
    {
        var resource = IcsResources.Split(File.ReadAllText(IcsResourcesTests.Corpus("outlook-2003.ics"))).Resources.Single();

        var list = OccurrenceExpander.Expand(
            Guid.Empty, Guid.Empty, IcsDocument.TryLoad(resource)!,
            new DateTime(2009, 3, 9, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2009, 3, 11, 0, 0, 0, DateTimeKind.Utc), "Europe/Brussels", "Europe/Brussels");

        Assert.Equal(new DateTime(2009, 3, 9, 22, 30, 0, DateTimeKind.Utc), list[0].StartUtc);
        Assert.Equal(new DateTime(2009, 3, 9, 22, 45, 0, DateTimeKind.Utc), list[0].EndUtc);
        Assert.Equal("20090310T093000", list[0].InstanceId);
        Assert.Null(list[0].TimeZone);
        Assert.False(list[0].IsFloating);
    }

    /// <summary>
    /// La seule chose que `tz` décide : une instance flottante à 23:00 le 30 septembre tombe dans
    /// la fenêtre vue de Bruxelles (21:00 UTC) et hors d'elle vue de Los Angeles (06:00 UTC le 1er).
    /// </summary>
    [Fact]
    public void Floating_AtTheWindowEdge_IsCutByTheViewZone()
    {
        var ics = Ics.Single(start: "DTSTART:20260930T230000", end: "DTEND:20261001T000000");

        Assert.Single(Expand(ics, view: "Europe/Brussels"));
        Assert.Empty(Expand(ics, view: "America/Los_Angeles"));
    }

    /// <summary>Une exception déplacée hors de la fenêtre en sort : la fenêtre de septembre ne
    /// voit plus le 14, parti au 30 novembre, et ne le remplace pas par l'instance d'origine.</summary>
    [Fact]
    public void AnOverrideMovedOutOfTheWindow_LeavesIt()
    {
        var list = Expand(Ics.RuleWithOverride("FREQ=WEEKLY;COUNT=4", "20261130T090000", summary: "Standup"));

        Assert.Equal([7, 21, 28], list.Select(o => o.StartUtc!.Value.Day));
        Assert.DoesNotContain(list, o => o.IsOverride);
    }

    [Fact]
    public void Alarm_And_RecurrenceText_AreCarried()
    {
        var o = Expand(Ics.Rule("FREQ=WEEKLY;BYDAY=MO",
            extra: "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nDESCRIPTION:x\r\nEND:VALARM")).First();

        Assert.True(o.HasAlarm);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO", o.RecurrenceText);
        Assert.Equal("OPAQUE", o.Transparency);
    }

    /// <summary>
    /// La reculée d'automne : 02:30 se lit deux fois le 25 octobre 2026 à Bruxelles. La série
    /// quotidienne garde ses quatre jours, et l'heure murale reste 02:30 de part et d'autre.
    /// </summary>
    [Fact]
    public void Daily_At_0230_CrossesTheAutumnFallBack()
    {
        var list = Expand(Daily("20261024T023000", "20261024T033000"),
            new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(
        [
            new DateTime(2026, 10, 24, 0, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc),   // encore +02:00, la première lecture
            new DateTime(2026, 10, 26, 1, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 27, 1, 30, 0, DateTimeKind.Utc),
        ], list.Select(o => o.StartUtc!.Value));
        Assert.Equal(["20261024T023000", "20261025T023000", "20261026T023000", "20261027T023000"],
            list.Select(o => o.InstanceId));
    }

    /// <summary>
    /// Le saut de printemps : 02:30 n'existe pas le 29 mars 2026. Ical.Net pousse cette occurrence
    /// à 03:30, et l'identifiant d'instance suit l'heure murale réellement produite.
    /// </summary>
    [Fact]
    public void Daily_At_0230_CrossesTheSpringForward()
    {
        var list = Expand(Daily("20260328T023000", "20260328T033000"),
            new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(
        [
            new DateTime(2026, 3, 28, 1, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc),   // 03:30 locale : le trou est franchi
            new DateTime(2026, 3, 30, 0, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 31, 0, 30, 0, DateTimeKind.Utc),
        ], list.Select(o => o.StartUtc!.Value));
        Assert.Equal("20260329T033000", list[1].InstanceId);
    }

    /// <summary>
    /// Un EXDATE posé sur l'instance qu'un override remplace : Ical.Net 5.2.3 retire l'instance que
    /// la règle produisait et garde quand même le composant d'exception, qui apporte la sienne. Le
    /// 14 reste donc à 11:00, déplacé, jamais à 09:00 — ce que ce test fixe, faute d'une réponse
    /// que la RFC 5545 tranche.
    /// </summary>
    [Fact]
    public void ExdateOnAnOverriddenInstance_LeavesTheOverrideStanding()
    {
        var list = Expand(Ics.RuleWithOverride("FREQ=WEEKLY", "20260914T110000",
            extra: "EXDATE;TZID=" + Ics.Zone + ":20260914T090000", summary: "Standup"));

        Assert.Equal([7, 14, 21, 28], list.Select(o => o.StartUtc!.Value.Day));
        var kept = list.Single(o => o.StartUtc!.Value.Day == 14);
        Assert.True(kept.IsOverride);
        Assert.Equal(9, kept.StartUtc!.Value.Hour);
    }

    [Fact]
    public void Weekly_ExdateInsideTheAmbiguousHour_RemovesThatOccurrence()
    {
        var list = Expand(Ics.Single(start: "DTSTART;TZID=Europe/Brussels:20261011T023000",
                end: "DTEND;TZID=Europe/Brussels:20261011T033000",
                extra: "RRULE:FREQ=WEEKLY;COUNT=4\r\nEXDATE;TZID=Europe/Brussels:20261025T023000"),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(
        [
            new DateTime(2026, 10, 11, 0, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 18, 0, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Utc),
        ], list.Select(o => o.StartUtc!.Value));
    }

    /// <summary>
    /// La règle sous-journalière en zone nommée tue le processus si on la déroule : l'expansion la
    /// refuse au garde et ne rend que le maître. Un test qui la déroulerait ne rougirait pas, il
    /// emporterait la suite entière.
    /// </summary>
    [Fact]
    public void SubDailyZonedRule_IsRefused_AndYieldsTheMasterAlone()
    {
        var list = Expand(Ics.Rule("FREQ=HOURLY"));

        var only = Assert.Single(list);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), only.StartUtc);
        Assert.Equal(new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc), only.EndUtc);
        Assert.Equal("20260907T090000", only.InstanceId);
        Assert.Equal("FREQ=HOURLY", only.RecurrenceText);
    }

    [Fact]
    public void OverrideCarriesItsOwnSummary_TheMasterKeepsItsOwn()
    {
        var list = Expand(Ics.WeeklyWithExdateAndOverride());

        Assert.Equal("Standup (moved)", list.Single(o => o.IsOverride).Summary);
        Assert.All(list.Where(o => !o.IsOverride), o => Assert.Equal("Standup", o.Summary));
    }

    /// <summary>
    /// La fenêtre balaie tous les agendas d'un utilisateur d'un coup : sans ces deux identifiants
    /// sur chaque instance, le client ne peut ni filtrer ni colorer sans une seconde lecture.
    /// </summary>
    [Fact]
    public void EveryInstance_CarriesTheRowItCameFrom()
    {
        var eventId = Guid.NewGuid();
        var calendarId = Guid.NewGuid();

        var list = OccurrenceExpander.Expand(
            eventId, calendarId, IcsDocument.TryLoad(Ics.WeeklyWithExdateAndOverride())!,
            From, To, "Europe/Brussels", "Europe/Brussels");

        Assert.NotEmpty(list);
        Assert.All(list, o =>
        {
            Assert.Equal(eventId, o.EventId);
            Assert.Equal(calendarId, o.CalendarId);
        });
    }

    // ---- the VALARM REPEAT/DURATION repetitions --------------------------------------------

    [Fact]
    public void ARepeatedAlarm_FiresAtEachRepetition()
    {
        // RFC 5545 § 3.8.6.2: REPEAT:5 with DURATION:PT10M is five MORE rings after the trigger.
        // The event starts at 00:00, the trigger is an hour before, so the six rings run
        // 23:00, 23:10 … 23:50 of the day before.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:5", "DURATION:PT10M"));

        Assert.True(Fires(parsed, "20270101T234500Z", "20270102T000000Z"));   // the fifth ring
        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));   // the trigger itself
        Assert.False(Fires(parsed, "20270102T000500Z", "20270102T010000Z"));  // past the last one
    }

    [Fact]
    public void ARepeatWithoutADuration_RingsOnce()
    {
        // § 3.8.6.2 makes the two inseparable: REPEAT alone spaces nothing.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z", "TRIGGER;RELATED=START:-PT1H", "REPEAT:5"));

        Assert.False(Fires(parsed, "20270101T234500Z", "20270102T000000Z"));
        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));
    }

    [Fact]
    public void AnAbsurdRepeatCount_IsBounded()
    {
        // A body may name REPEAT:1000000. The walk that answers a query must stay finite whatever
        // it is handed; the window decides, never the file.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:1000000", "DURATION:PT1S"));

        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));
    }

    // ---- an absent bound ------------------------------------------------------------------

    [Fact]
    public void AnAbsentStart_WalksTheFileFromItsOwnFirstInstant()
    {
        // Opened at DateTime.MinValue the engine throws « un-representable DateTime », the walk's
        // blanket catch reads it as no occurrence, and every recurring resource vanishes from the
        // 207 — the mirror of the closure this slice removes. reports.xml/time-range t14.
        var end = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(OccurrenceExpander.Overlaps(Load(Ics.Rule("FREQ=WEEKLY")), null, end, Ics.Zone));
        Assert.True(OccurrenceExpander.Overlaps(
            Load(Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z")), null, end, Ics.Zone));
    }

    [Fact]
    public void AnAbsentStart_SeesAnInstanceOlderThanTheFiveYearsTheEngineWalks()
    {
        // The floor is the file's own earliest instant, never a span back from the end: a series
        // begun in 1990 answers a window that names no start at all.
        var parsed = Load(Ics.Single("DTSTART:19900315T090000Z", "DTEND:19900315T100000Z"));

        Assert.True(OccurrenceExpander.Overlaps(parsed, null, Instant("20261001T000000Z"), Ics.Zone));
        Assert.False(OccurrenceExpander.Overlaps(parsed, null, Instant("19900315T080000Z"), Ics.Zone));
    }

    [Fact]
    public void AnAbsentStart_SeesAnRDatePeriodOlderThanTheMaster()
    {
        // RDATE;VALUE=PERIOD is read by GetAllPeriods and never by GetAllDates: a floor blind to it
        // sits ABOVE an instance the file really produces, and the resource leaves the 207 in
        // silence — the very loss the open bound exists to stop.
        var parsed = Load(Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z",
            "RDATE;VALUE=PERIOD:19900315T090000Z/PT1H"));
        var end = Instant("20200101T000000Z");

        Assert.True(OccurrenceExpander.Overlaps(parsed, null, end, Ics.Zone));
        Assert.True(OccurrenceExpander.Overlaps(parsed, Instant("19900101T000000Z"), end, Ics.Zone));
    }

    [Fact]
    public void AMasterRepeatingByPeriodAlone_IsRecurring_AndItsInstancesCarryTheirIds()
    {
        // The floor reads RDATE;VALUE=PERIOD; the « this master repeats » predicate must read the
        // same file, or its instances walk with an empty id while the walk itself serves two.
        var list = Expand(Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z",
            "RDATE;VALUE=PERIOD:20260914T090000Z/PT1H"));

        Assert.Equal(["20260907T090000Z", "20260914T090000Z"], list.Select(o => o.InstanceId));
    }

    [Fact]
    public void AnAbsentStart_OnAFileAtTheEdgeOfTime_StillWalks()
    {
        // The floor keeps two margins above DateTime.MinValue: its own, and the one Periods adds.
        var parsed = Load(Ics.Single("DTSTART:00010101T000000Z", "DTEND:00010101T010000Z", "RRULE:FREQ=WEEKLY"));

        Assert.True(OccurrenceExpander.Overlaps(parsed, null, Instant("20261001T000000Z"), Ics.Zone));
    }

    [Fact]
    public void AnAbsentStart_IsNotAnExcuseToMatchEverything()
    {
        var parsed = Load(Ics.Single("DTSTART:20320315T090000Z", "DTEND:20320315T100000Z"));

        Assert.False(OccurrenceExpander.Overlaps(parsed, null, Instant("20260907T000000Z"), Ics.Zone));
    }

    [Theory]
    [InlineData("DURATION:PT0S")]
    [InlineData("DURATION:-PT10M")]
    public void ARepeatWhoseSpacingRingsNowhere_RingsOnce(string duration)
    {
        // A spacing of nothing would ring at one instant for ever, a backwards one would walk away
        // from the window: § 3.8.6.2 gives neither a meaning, so the trigger stands alone.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:5", duration));

        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));
        Assert.False(Fires(parsed, "20270101T234500Z", "20270102T000000Z"));
    }

    [Fact]
    public void ANegativeRepeat_RingsOnce()
    {
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:-1", "DURATION:PT10M"));

        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));
        Assert.False(Fires(parsed, "20270101T234500Z", "20270102T000000Z"));
    }

    [Fact]
    public void ADurationWithoutARepeat_RingsOnce()
    {
        // The other direction of the inseparability: a spacing spaces nothing on its own.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "DURATION:PT10M"));

        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));
        Assert.False(Fires(parsed, "20270101T234500Z", "20270102T000000Z"));
    }

    [Fact]
    public void ARingOnTheWindowsOwnEdge_FiresOnTheLeftAndNotOnTheRight()
    {
        // The jump to the first ring the window can hold is arithmetic, so its edges are where it
        // can be wrong by one. The window is [fromUtc, toUtc[ : closed left, open right.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:5", "DURATION:PT10M"));
        var ring = Instant("20270101T233000Z");   // 23:00 + three spacings, the fourth of six

        Assert.True(Fires(parsed, ring, ring.AddTicks(1)));                    // ON the start: fires
        Assert.False(Fires(parsed, ring.AddTicks(1), ring.AddMinutes(5)));     // a tick before: not
        Assert.False(Fires(parsed, ring.AddMinutes(-5), ring));                // ON the end: not
    }

    [Fact]
    public void TheRings_SkipTheOnesTheWindowLeavesBehind()
    {
        // A million rings a second apart run for eleven and a half days from the trigger; the
        // window holds rings 82 800 to 86 399, far past MaxRings. The rings before it are counted,
        // not walked, and the bound counts the rings walked, never their index — or every window
        // past the ten-thousandth ring would answer false while the file rings every second there.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-P1D", "REPEAT:1000000", "DURATION:PT1S"));

        Assert.True(Fires(parsed, "20270101T230000Z", "20270102T000000Z"));
        Assert.True(Fires(parsed, "20270101T000000Z", "20270101T000100Z"));    // the trigger itself
        Assert.False(Fires(parsed, "20270113T140000Z", "20270114T000000Z"));   // past the millionth
    }

    [Fact]
    public void ABoundedWindowPastTheTenThousandthRing_StillHearsTheAlarm()
    {
        // A trigger eight days ahead of its instance, ringing every minute for two weeks: the
        // instance's own hour holds rings 11 520 to 11 579. The instance is inside the day the walk
        // allows on each side, so only the ring bound could lose it.
        var parsed = Load(Ics.Alarmed("DTSTART:20270110T000000Z",
            "TRIGGER;RELATED=START:-P8D", "REPEAT:20000", "DURATION:PT1M"));

        Assert.True(Fires(parsed, "20270110T000000Z", "20270110T010000Z"));
    }

    [Fact]
    public void TheRings_StopAtTheWindowsEnd_RatherThanAtTheFilesCount()
    {
        // A million rings a second apart, all of them past a window that ends on the trigger: the
        // sequence is monotonic, so the first one past the end ends the walk instead of MaxRings.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:1000000", "DURATION:PT1S"));

        Assert.False(Fires(parsed, "20270101T220000Z", "20270101T230000Z"));
        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230001Z"));
    }

    [Fact]
    public void AnAllDayInstance_IsPosedInTheZoneItIsJudgedIn()
    {
        // RFC 4791 § 9.9: a DATE value is resolved in the request's CALDAV:timezone, else the
        // collection's. January 1st in New York runs from 05:00Z to 05:00Z, not midnight to midnight.
        var parsed = Load(Ics.Single("DTSTART;VALUE=DATE:20270101", "DTEND;VALUE=DATE:20270102"));

        Assert.True(OccurrenceExpander.Overlaps(parsed,
            Instant("20270102T000000Z"), Instant("20270102T040000Z"), "America/New_York"));
        Assert.False(OccurrenceExpander.Overlaps(parsed,
            Instant("20270102T060000Z"), Instant("20270102T070000Z"), "America/New_York"));
    }

    [Fact]
    public void AnAllDayInstance_InUtc_IsUnchanged()
    {
        // Every caller that passes UTC — the API's default view — sees exactly what it saw before.
        var parsed = Load(Ics.Single("DTSTART;VALUE=DATE:20270101", "DTEND;VALUE=DATE:20270102"));

        Assert.True(OccurrenceExpander.Overlaps(parsed,
            Instant("20270101T000000Z"), Instant("20270101T010000Z"), IcsTimeZones.Utc));
        Assert.False(OccurrenceExpander.Overlaps(parsed,
            Instant("20261231T230000Z"), Instant("20270101T000000Z"), IcsTimeZones.Utc));
    }

    private static string Daily(string start, string end) =>
        Ics.Single(start: "DTSTART;TZID=Europe/Brussels:" + start, end: "DTEND;TZID=Europe/Brussels:" + end,
            extra: "RRULE:FREQ=DAILY;COUNT=4");

    private static IReadOnlyList<EventOccurrence> Expand(string ics, DateTime? from = null, DateTime? to = null, string view = "Europe/Brussels") =>
        OccurrenceExpander.Expand(Guid.Empty, Guid.Empty, IcsDocument.TryLoad(ics)!, from ?? From, to ?? To, "Europe/Brussels", view);

    private static IcsCalendar Load(string ics) => IcsDocument.TryLoad(ics)!;

    private static bool Fires(IcsCalendar parsed, string from, string to) =>
        Fires(parsed, Instant(from), Instant(to));

    private static bool Fires(IcsCalendar parsed, DateTime from, DateTime to) =>
        OccurrenceExpander.AlarmFires(parsed, from, to, "UTC");

    private static DateTime Instant(string value) => DateTime.ParseExact(
        value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
