using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// What the editor actually receives. <see cref="EventResponse"/> is the wire shape and
/// <see cref="EventDetail"/> the store's; a field added to the second and forgotten on the first
/// is a positional record still compiling, a client field silently undefined, and a screen that
/// throws where it reads a list.
/// </summary>
public sealed class EventResponseContractTests
{
    /// <summary>Verbatim from a Samsung Calendar event synchronised by DAVx5 4.5.19 (ical4j 4.3.0),
    /// as the PUT stored it — no STATUS, no VALARM, no ATTENDEE, no RRULE, no URL.</summary>
    private const string SamsungViaDavx5 =
        "BEGIN:VCALENDAR\r\n" +
        "VERSION:2.0\r\n" +
        "PRODID:DAVx5/4.5.19-gplay ical4j/4.3.0\r\n" +
        "BEGIN:VEVENT\r\n" +
        "DTSTAMP:20260907T134622Z\r\n" +
        "UID:555b3408-9483-4a78-9c6c-f6e51b831650\r\n" +
        "SUMMARY:Hello world\r\n" +
        "LOCATION:Thier des Trixhes 183\\, 4400 Flémalle\r\n" +
        "DTSTART;TZID=Europe/Brussels:20260907T160000\r\n" +
        "DTEND;TZID=Europe/Brussels:20260907T170000\r\n" +
        "DESCRIPTION:This is a test\r\n" +
        "END:VEVENT\r\n" +
        "BEGIN:VTIMEZONE\r\n" +
        "TZID:Europe/Brussels\r\n" +
        "BEGIN:STANDARD\r\n" +
        "TZNAME:CET\r\nTZOFFSETFROM:+0200\r\nTZOFFSETTO:+0100\r\n" +
        "DTSTART:19961027T030000\r\nRRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU\r\n" +
        "END:STANDARD\r\n" +
        "BEGIN:DAYLIGHT\r\n" +
        "TZNAME:CEST\r\nTZOFFSETFROM:+0100\r\nTZOFFSETTO:+0200\r\n" +
        "DTSTART:19810329T020000\r\nRRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU\r\n" +
        "END:DAYLIGHT\r\n" +
        "END:VTIMEZONE\r\n" +
        "END:VCALENDAR\r\n";

    /// <summary>The API's own policy (MvcFormatterConfiguration): a null field is absent from the
    /// wire, so anything the client declares non-optional must never be null — nor missing.</summary>
    private static readonly JsonSerializerOptions Wire = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The pincer: every property of the store's shape is relayed by the wire's. Written by
    /// reflection rather than listed, so a field added to EventDetail turns this red instead of
    /// being dropped by a positional constructor that still compiles.
    /// </summary>
    [Fact]
    public void TheWireShape_RelaysEveryFieldOfTheStoreShape()
    {
        var stored = typeof(EventDetail).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Order().ToArray();
        var wire = typeof(EventResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Order().ToArray();

        Assert.Equal(stored, wire);
    }

    [Fact]
    public void AnEventTheEditorOpens_CarriesTheTwoListsAndFlagsItsScreenReads()
    {
        var parsed = IcsDocument.TryLoad(SamsungViaDavx5);
        Assert.NotNull(parsed);

        var detail = new EventDetail(
            Guid.NewGuid(), Guid.NewGuid(), "555b3408-9483-4a78-9c6c-f6e51b831650", "hash",
            IcsReader.Read(parsed, Guid.NewGuid()),
            IcsDocument.MasterOf(parsed)?.RecurrenceRule?.ToString(), [], null,
            IcsReader.RepeatIsExact(parsed), IcsReader.ForeignAlarms(parsed));

        var wire = JsonSerializer.SerializeToNode(EventResponse.From(detail), Wire)!.AsObject();

        // The editor spreads fields.reminderMinutesBefore, reads .length on foreignAlarms and
        // attendees, and negates repeatIsExact into keepRepeat: absent from the wire is `undefined`
        // in the browser, so a missing list throws and a missing flag locks every rule.
        Assert.True(wire.ContainsKey("attendees"), "attendees is absent from the wire");
        Assert.True(wire.ContainsKey("foreignAlarms"), "foreignAlarms is absent from the wire");
        Assert.True(wire.ContainsKey("repeatIsExact"), "repeatIsExact is absent from the wire");
        Assert.True(wire["fields"]!.AsObject().ContainsKey("reminderMinutesBefore"),
            "fields.reminderMinutesBefore is absent from the wire");
    }

    [Fact]
    public void AResourceWrittenByAPhone_ReadsBackItsOwnHoursAndZone()
    {
        var parsed = IcsDocument.TryLoad(SamsungViaDavx5)!;

        var fields = IcsReader.Read(parsed, Guid.NewGuid());

        Assert.False(fields.IsAllDay);
        Assert.Equal("Europe/Brussels", fields.TimeZone);
        Assert.Equal(new DateTime(2026, 9, 7, 16, 0, 0, DateTimeKind.Unspecified), fields.Start);
        Assert.Equal(new DateTime(2026, 9, 7, 17, 0, 0, DateTimeKind.Unspecified), fields.End);
        Assert.Equal("Hello world", fields.Summary);
        Assert.Equal("This is a test", fields.Description);
    }
}
