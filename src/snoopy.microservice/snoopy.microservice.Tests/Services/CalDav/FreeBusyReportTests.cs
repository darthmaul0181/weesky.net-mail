using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

/// <summary>§ 9: TRANSPARENT and CANCELLED dropped, TENTATIVE becomes BUSY-TENTATIVE, same-type
/// periods coalesced, different types never merged — on <see cref="EventOccurrence"/> directly,
/// the way <see cref="OccurrenceExpander"/> hands them over.</summary>
public sealed class FreeBusyReportTests
{
    private static readonly DateTime Base = new(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TwoTouchingPeriodsOfTheSameType_Coalesce()
    {
        var periods = FreeBusyReport.Periods(
        [
            Timed(Base, Base.AddHours(1), status: null),
            Timed(Base.AddHours(1), Base.AddHours(2), status: null),
        ], Ics.Zone);

        var period = Assert.Single(periods);
        Assert.Equal(Base, period.StartUtc);
        Assert.Equal(Base.AddHours(2), period.EndUtc);
        Assert.Equal(FreeBusyReport.Busy, period.Type);
    }

    [Fact]
    public void TwoOverlappingPeriodsOfTheSameType_Coalesce()
    {
        var periods = FreeBusyReport.Periods(
        [
            Timed(Base, Base.AddHours(2), status: null),
            Timed(Base.AddHours(1), Base.AddHours(3), status: null),
        ], Ics.Zone);

        var period = Assert.Single(periods);
        Assert.Equal(Base, period.StartUtc);
        Assert.Equal(Base.AddHours(3), period.EndUtc);
    }

    [Fact]
    public void OverlappingBusyAndBusyTentative_AreNeverCoalesced()
    {
        var periods = FreeBusyReport.Periods(
        [
            Timed(Base, Base.AddHours(2), status: null),
            Timed(Base.AddHours(1), Base.AddHours(3), status: "TENTATIVE"),
        ], Ics.Zone);

        Assert.Equal(2, periods.Count);
        Assert.Equal(FreeBusyReport.Busy, periods[0].Type);
        Assert.Equal(FreeBusyReport.BusyTentative, periods[1].Type);
        Assert.Equal(Base, periods[0].StartUtc);
        Assert.Equal(Base.AddHours(1), periods[1].StartUtc);
    }

    [Fact]
    public void ATransparentInstance_IsExcluded()
    {
        var periods = FreeBusyReport.Periods(
            [Timed(Base, Base.AddHours(1), status: null, transparency: "TRANSPARENT")], Ics.Zone);

        Assert.Empty(periods);
    }

    [Fact]
    public void ACancelledInstance_IsExcluded()
    {
        var periods = FreeBusyReport.Periods(
            [Timed(Base, Base.AddHours(1), status: "CANCELLED")], Ics.Zone);

        Assert.Empty(periods);
    }

    [Fact]
    public void ATentativeInstance_BecomesBusyTentative()
    {
        var periods = FreeBusyReport.Periods(
            [Timed(Base, Base.AddHours(1), status: "TENTATIVE")], Ics.Zone);

        Assert.Equal(FreeBusyReport.BusyTentative, Assert.Single(periods).Type);
    }

    [Fact]
    public void AConfirmedOrPlainInstance_IsBusy()
    {
        var periods = FreeBusyReport.Periods(
            [Timed(Base, Base.AddHours(1), status: "CONFIRMED")], Ics.Zone);

        Assert.Equal(FreeBusyReport.Busy, Assert.Single(periods).Type);
    }

    [Fact]
    public void AnAllDayInstance_ReadsAsUtcMidnightOfItsDates()
    {
        var start = new DateOnly(2026, 9, 7);
        var end = new DateOnly(2026, 9, 9);
        var periods = FreeBusyReport.Periods([AllDay(start, end, status: null)], Ics.Zone);

        var period = Assert.Single(periods);
        Assert.Equal(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc), period.EndUtc);
    }

    [Fact]
    public void AFloatingInstance_IsPlacedInTheCalendarsOwnZone()
    {
        // 09:00-10:00 wall clock in Europe/Brussels, 7 September (DST, UTC+2).
        var local = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Unspecified);
        var periods = FreeBusyReport.Periods(
            [Floating(local, local.AddHours(1), status: null)], Ics.Zone);

        var period = Assert.Single(periods);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc), period.EndUtc);
    }

    [Fact]
    public void PeriodsOfDifferentTypesAreOrderedByStart_AcrossBothGroups()
    {
        var periods = FreeBusyReport.Periods(
        [
            Timed(Base.AddHours(3), Base.AddHours(4), status: "TENTATIVE"),
            Timed(Base, Base.AddHours(1), status: null),
        ], Ics.Zone);

        Assert.Equal([Base, Base.AddHours(3)], periods.Select(p => p.StartUtc));
    }

    [Fact]
    public void Compose_RoundTripsThroughAVFreeBusyOfTwoEntries()
    {
        var periods = new[]
        {
            new FreeBusyReport.BusyPeriod(Base, Base.AddHours(1), FreeBusyReport.Busy),
            new FreeBusyReport.BusyPeriod(Base.AddHours(2), Base.AddHours(3), FreeBusyReport.BusyTentative),
        };
        var text = FreeBusyReport.Compose(Base, Base.AddHours(4), periods, Base);

        var reloaded = IcsDocument.TryLoad(text);
        Assert.NotNull(reloaded);
        var freeBusy = Assert.Single(reloaded.FreeBusy);
        Assert.Equal(2, freeBusy.Entries.Count);
        Assert.Equal(Base, freeBusy.DtStart!.AsUtc);
        Assert.Equal(Base.AddHours(4), freeBusy.DtEnd!.AsUtc);
        Assert.Equal(Base, freeBusy.DtStamp!.AsUtc);
    }

    [Fact]
    public void Compose_WithNoBusyPeriods_StillWritesAVFreeBusyWithNone()
    {
        // RFC 4791 § 7.10: "If no calendar object resources are found ... a VFREEBUSY component
        // with no FREEBUSY property MUST be returned."
        var text = FreeBusyReport.Compose(Base, Base.AddHours(1), [], Base);

        var reloaded = IcsDocument.TryLoad(text);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded.FreeBusy);
        // On the text: Ical.Net 5.2.3 enumerates Entries as empty even when its Count is right.
        Assert.DoesNotContain(Lines(text), line => line.StartsWith("FREEBUSY"));
    }

    /// <summary>The lines of an iCalendar document. A bare Contains would match FREEBUSY inside
    /// BEGIN:VFREEBUSY, which every answer carries.</summary>
    private static string[] Lines(string ics) =>
        [.. ics.Split('\n').Select(line => line.TrimEnd('\r'))];

    private static EventOccurrence Timed(
        DateTime startUtc, DateTime endUtc, string? status, string transparency = "OPAQUE") =>
        new(Guid.Empty, Guid.Empty, "uid", "", false, false, false, "UTC", startUtc, endUtc,
            null, null, null, null, null, null, status, transparency, null, false, null);

    private static EventOccurrence AllDay(
        DateOnly start, DateOnly endExclusive, string? status, string transparency = "OPAQUE") =>
        new(Guid.Empty, Guid.Empty, "uid", "", false, true, false, null, null, null,
            start, endExclusive, null, null, null, null, status, transparency, null, false, null);

    private static EventOccurrence Floating(
        DateTime localStart, DateTime localEnd, string? status, string transparency = "OPAQUE") =>
        new(Guid.Empty, Guid.Empty, "uid", "", false, false, true, null, null, null,
            null, null, localStart, localEnd, null, null, status, transparency, null, false, null);
}
