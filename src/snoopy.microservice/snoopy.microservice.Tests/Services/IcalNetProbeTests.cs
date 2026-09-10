using System.Runtime.Serialization;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Each test pins one fact of Ical.Net 5.2.3 the calendar slices depend on. They are the contract
/// tasks 2-4 read their names from: a member that moves in a later upgrade fails here, not in a
/// service.
/// </summary>
public sealed class IcalNetProbeTests
{
    private const string Weekly = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//probe//EN
        BEGIN:VTIMEZONE
        TZID:Europe/Brussels
        BEGIN:STANDARD
        DTSTART:19701025T030000
        RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU
        TZOFFSETFROM:+0200
        TZOFFSETTO:+0100
        END:STANDARD
        BEGIN:DAYLIGHT
        DTSTART:19700329T020000
        RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU
        TZOFFSETFROM:+0100
        TZOFFSETTO:+0200
        END:DAYLIGHT
        END:VTIMEZONE
        BEGIN:VEVENT
        UID:probe-1
        DTSTAMP:20260901T080000Z
        DTSTART;TZID=Europe/Brussels:20260907T090000
        DTEND;TZID=Europe/Brussels:20260907T100000
        RRULE:FREQ=WEEKLY
        EXDATE;TZID=Europe/Brussels:20260921T090000
        SUMMARY:Standup
        X-APPLE-TRAVEL-ADVISORY-BEHAVIOR:AUTOMATIC
        END:VEVENT
        BEGIN:VEVENT
        UID:probe-1
        RECURRENCE-ID;TZID=Europe/Brussels:20260914T090000
        DTSTAMP:20260901T080000Z
        DTSTART;TZID=Europe/Brussels:20260914T110000
        DTEND;TZID=Europe/Brussels:20260914T120000
        SUMMARY:Standup (moved)
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Load_ReadsMasterAndOverride()
    {
        var calendar = Load(Weekly);

        Assert.Equal(2, calendar.Events.Count);
        var master = Master(calendar);
        Assert.NotNull(master.DtStart);
        Assert.Equal("Europe/Brussels", master.DtStart.TzId);
        Assert.True(master.DtStart.HasTime);
        // RecurrenceRules (plural) is obsolete in 5.2.3: one RRULE per component is the model now.
        Assert.NotNull(master.RecurrenceRule);
    }

    // Two doors, not one, and the PUT gate of task 2 needs both: an empty body answers null, a
    // malformed one throws. Neither is an exception the caller can skip.
    [Fact]
    public void Load_AnswersNullOnEmpty_AndThrowsOnGarbage()
    {
        Assert.Null(Calendar.Load(string.Empty));
        Assert.Throws<SerializationException>(() => Calendar.Load("not an icalendar at all"));
    }

    [Fact]
    public void Occurrences_AreLazy_ExdateRemoves_OverrideReplaces()
    {
        var calendar = Load(Weekly);
        var from = new CalDateTime(2026, 9, 1, 0, 0, 0, "UTC");
        var to = new CalDateTime(2026, 10, 1, 0, 0, 0, "UTC");

        var occurrences = calendar.GetOccurrences(from).TakeWhileBefore(to).ToList();

        // 7, 14 (moved to 11:00), 28 — 21 is EXDATEd.
        Assert.Equal(3, occurrences.Count);
        var moved = occurrences.Single(o => StartOf(o).Day == 14);
        Assert.Equal(11, StartOf(moved).Hour);
        Assert.Equal("Standup (moved)", Assert.IsType<CalendarEvent>(moved.Source).Summary);
    }

    [Fact]
    public void Occurrences_InfiniteRule_DoesNotEnumerateWithoutBound()
    {
        var calendar = Load(Weekly);
        var from = new CalDateTime(2026, 9, 1, 0, 0, 0, "UTC");

        var first = calendar.GetOccurrences(from).Take(5).ToList();

        Assert.Equal(5, first.Count);
    }

    [Fact]
    public void AsUtc_ResolvesIanaZoneThroughTzdb()
    {
        var master = Master(Load(Weekly));

        Assert.NotNull(master.DtStart);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), master.DtStart.AsUtc);
    }

    [Fact]
    public void Serialize_KeepsUnknownXProperty()
    {
        var calendar = Load(Weekly);

        var text = new CalendarSerializer().SerializeToString(calendar);

        Assert.Contains("X-APPLE-TRAVEL-ADVISORY-BEHAVIOR:AUTOMATIC", text);
        Assert.Contains("EXDATE;TZID=Europe/Brussels:20260921T090000", text);
    }

    [Fact]
    public void VTimeZone_FromTzdb_CarriesTransitionRules()
    {
        var calendar = new Calendar();
        calendar.AddTimeZone(VTimeZone.FromDateTimeZone("Europe/Brussels", new DateTime(2026, 1, 1), false));

        var text = new CalendarSerializer().SerializeToString(calendar);

        Assert.Contains("BEGIN:STANDARD", text);
        Assert.Contains("BEGIN:DAYLIGHT", text);
        Assert.Contains("TZID:Europe/Brussels", text);
        // Compact rules, not an expanded list of every past transition: the third argument is
        // includeHistoricalData, and false is what keeps one VTIMEZONE from dwarfing its event.
        Assert.Contains("RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10", text);
    }

    [Fact]
    public void WindowsTzid_IsNotKnownToTzdb_ButMappable()
    {
        Assert.Null(NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull("Romance Standard Time"));

        var mapping = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping;

        Assert.Equal("Europe/Paris", mapping["Romance Standard Time"]);
    }

    [Fact]
    public void ThisAndFuture_IsParsedButNotAppliedByExpansion()
    {
        var text = Weekly.Replace("RECURRENCE-ID;TZID=Europe/Brussels:20260914T090000",
                                  "RECURRENCE-ID;RANGE=THISANDFUTURE;TZID=Europe/Brussels:20260914T090000");
        var calendar = Load(text);
        var from = new CalDateTime(2026, 9, 1, 0, 0, 0, "UTC");
        var to = new CalDateTime(2026, 10, 1, 0, 0, 0, "UTC");

        var over = calendar.Events.Single(e => e.RecurrenceIdentifier is not null);
        Assert.Equal(RecurrenceRange.ThisAndFuture, over.RecurrenceIdentifier?.Range);

        var day28 = calendar.GetOccurrences(from).TakeWhileBefore(to).Single(o => StartOf(o).Day == 28);
        // Issue #455 : le RANGE se lit mais l'expansion ne l'applique pas. Si ce test devient FAUX
        // (28 à 11:00), la lacune s'est fermée — le rapport le dit.
        Assert.Equal(9, StartOf(day28).Hour);
    }

    private static Calendar Load(string ics)
    {
        var calendar = Calendar.Load(ics);
        Assert.NotNull(calendar);
        return calendar;
    }

    private static CalendarEvent Master(Calendar calendar) =>
        calendar.Events.Single(e => e.RecurrenceIdentifier is null);

    private static CalDateTime StartOf(Occurrence occurrence)
    {
        Assert.NotNull(occurrence.Period.StartTime);
        return occurrence.Period.StartTime;
    }
}
