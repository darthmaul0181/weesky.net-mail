using Ical.Net;
using Ical.Net.CalendarComponents;
using weesky.Snoopy.Microservice.Services.Calendar;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IcsTimeZonesTests
{
    [Theory]
    [InlineData("Europe/Brussels", "Europe/Brussels")]
    [InlineData("Romance Standard Time", "Europe/Paris")]
    [InlineData("(UTC+01:00) Bruxelles, Copenhague, Madrid, Paris", null)]
    [InlineData("Nowhere/Land", null)]
    [InlineData(null, null)]
    public void ResolveIana(string? tzid, string? expected) => Assert.Equal(expected, IcsTimeZones.ResolveIana(tzid));

    [Fact]
    public void IsKnownIana_AnswersTheFirstTierOnly()
    {
        Assert.True(IcsTimeZones.IsKnownIana("Europe/Brussels"));
        Assert.True(IcsTimeZones.IsKnownIana(IcsTimeZones.Utc));
        Assert.False(IcsTimeZones.IsKnownIana("Romance Standard Time"));
    }

    [Fact]
    public void ToUtc_FollowsTzdbNotHost()
    {
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc),
            IcsTimeZones.ToUtc(new DateTime(2026, 3, 29, 3, 30, 0), "Europe/Brussels")); // première demi-heure d'été
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            IcsTimeZones.ToUtc(new DateTime(2026, 9, 7, 9, 0, 0), "Europe/Brussels"));
    }

    [Fact]
    public void FromUtc_IsTheOtherDirection() =>
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0),
            IcsTimeZones.FromUtc(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), "Europe/Brussels"));

    [Fact]
    public void Emit_CarriesRules()
    {
        var text = IcsDocument.Serialize(WithZone(IcsTimeZones.Emit("Europe/Brussels", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));

        Assert.Contains("BEGIN:DAYLIGHT", text);
        Assert.Contains("TZOFFSETTO:+0200", text);
    }

    /// <summary>Ical.Net 5.2.3 writes a DAYLIGHT block an hour off when the earliest instant falls
    /// inside daylight time: September in Brussels, January in Sydney, July in New York.</summary>
    [Theory]
    [InlineData("Europe/Brussels", 9, 1, 2)]
    [InlineData("Australia/Sydney", 1, 10, 11)]
    [InlineData("America/New_York", 7, -5, -4)]
    public void Emit_FromInsideDaylight_WritesTheRightOffsets(string zone, int month, int standardHours, int daylightHours)
    {
        var emitted = IcsTimeZones.Emit(zone, new DateTime(2025, month, 6, 7, 0, 0, DateTimeKind.Utc));

        var daylight = Assert.Single(emitted.TimeZoneInfos, i => i.Name == "DAYLIGHT");
        Assert.Equal(TimeSpan.FromHours(standardHours), daylight.OffsetFrom?.Offset);
        Assert.Equal(TimeSpan.FromHours(daylightHours), daylight.OffsetTo?.Offset);
        var standard = Assert.Single(emitted.TimeZoneInfos, i => i.Name == "STANDARD");
        Assert.Equal(TimeSpan.FromHours(daylightHours), standard.OffsetFrom?.Offset);
        Assert.Equal(TimeSpan.FromHours(standardHours), standard.OffsetTo?.Offset);
    }

    private static Calendar WithZone(VTimeZone zone)
    {
        var calendar = new Calendar();
        calendar.AddTimeZone(zone);
        return calendar;
    }
}
