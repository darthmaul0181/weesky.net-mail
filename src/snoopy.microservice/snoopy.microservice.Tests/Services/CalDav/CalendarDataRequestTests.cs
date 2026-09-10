using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

/// <summary>What a <c>CALDAV:calendar-data</c> element in a report body asks for: the file as
/// stored, or its instances over a window (RFC 4791 § 9.6.5) — and nothing else, § 9.6.1's
/// partial retrieval being deliberately unserved.</summary>
public sealed class CalendarDataRequestTests
{
    private static readonly XName CalendarData = DavXml.CalDav + "calendar-data";
    private static readonly XName GetEtag = DavXml.Dav + "getetag";

    private static readonly DavEvent Event = new(Guid.NewGuid(), Guid.NewGuid(), "a.ics", "rule",
        Ics.Rule("FREQ=WEEKLY;COUNT=3"), "abc", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 1);

    [Fact]
    public void ABodyNamingNoCalendarData_AsksForNone()
    {
        Assert.Null(CalendarDataRequest.Asked(Body(new XElement(GetEtag))));
    }

    [Fact]
    public void ABareCalendarData_IsTheFileAsStored()
    {
        var asked = CalendarDataRequest.Asked(Body(new XElement(GetEtag), new XElement(CalendarData)));

        Assert.NotNull(asked);
        Assert.False(asked.Expands);
        var element = asked.Element(Event, Ics.Zone);
        Assert.Equal(CalendarData, element.Name);
        Assert.Equal(Event.IcsRaw, element.Value);
    }

    [Fact]
    public void AnExpandWithBothBounds_IsReadAsUtcInstants()
    {
        var asked = CalendarDataRequest.Asked(Body(Expanded("20260901T000000Z", "20261001T000000Z")));

        Assert.NotNull(asked);
        Assert.True(asked.Expands);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), asked.ExpandFrom);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), asked.ExpandTo);
        Assert.Equal(DateTimeKind.Utc, asked.ExpandFrom!.Value.Kind);
    }

    [Fact]
    public void AnExpandedElement_CarriesTheInstancesAndNoRule()
    {
        var asked = CalendarDataRequest.Asked(Body(Expanded("20260901T000000Z", "20261001T000000Z")))!;

        var text = asked.Element(Event, Ics.Zone).Value;

        Assert.DoesNotContain("RRULE", text);
        Assert.Contains("RECURRENCE-ID:20260914T070000Z", text);
    }

    [Theory]
    [InlineData("20260901T000000Z", null)]
    [InlineData(null, "20261001T000000Z")]
    [InlineData(null, null)]
    [InlineData("2026-09-01", "20261001T000000Z")]
    [InlineData("20260901T000000", "20261001T000000Z")]
    [InlineData("20261001T000000Z", "20260901T000000Z")]
    [InlineData("20260901T000000Z", "20260901T000000Z")]
    public void AnExpandThatNamesNoWindow_IsRefusedAsValidFilter(string? start, string? end)
    {
        // Both bounds are mandatory and UTC (§ 9.6.5), and a window has to be one: refused before
        // anything is written, under the element a client can read.
        var refused = Assert.Throws<DavPreconditionException>(
            () => CalendarDataRequest.Asked(Body(Expanded(start, end))));

        Assert.Equal(CalDavError.ValidFilter, refused.Condition);
    }

    [Theory]
    [InlineData("text/calendar", "2.0")]
    [InlineData("text/calendar; charset=utf-8", null)]
    [InlineData(null, "2.0")]
    public void TheAnnouncedContentTypeAndVersion_AreAccepted(string? contentType, string? version)
    {
        var asked = CalendarDataRequest.Asked(Body(Typed(contentType, version)));

        Assert.NotNull(asked);
        Assert.False(asked.Expands);
    }

    [Theory]
    [InlineData("text/vcard", null)]
    [InlineData(null, "1.0")]
    public void AMediaTypeOrVersionWeDoNotAnnounce_IsRefusedAsSupportedCalendarData(
        string? contentType, string? version)
    {
        var refused = Assert.Throws<DavPreconditionException>(
            () => CalendarDataRequest.Asked(Body(Typed(contentType, version))));

        Assert.Equal(CalDavError.SupportedCalendarData, refused.Condition);
    }

    [Fact]
    public void CompAndPropChildren_AreIgnoredAndTheWholeFileServed()
    {
        // § 9.6.1's partial retrieval is not served (spec § 7): none of the clients aimed at asks
        // for it, and more than asked is not an error to a client — a refusal would be.
        var partial = new XElement(CalendarData,
            new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VCALENDAR"),
                new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VEVENT"),
                    new XElement(DavXml.CalDav + "prop", new XAttribute("name", "SUMMARY")))));

        var asked = CalendarDataRequest.Asked(Body(partial));

        Assert.NotNull(asked);
        Assert.False(asked.Expands);
        Assert.Equal(Event.IcsRaw, asked.Element(Event, Ics.Zone).Value);
    }

    [Fact]
    public void PropertiesAsked_LeavesCalendarDataToTheResolver()
    {
        var request = CalendarDataRequest.PropertiesAsked(
            Body(new XElement(GetEtag), new XElement(CalendarData)));

        // Left in, the event table would serve the stored file beside the expansion — or, on a
        // property the table holds, twice.
        Assert.Equal([GetEtag], request.Names);
    }

    [Fact]
    public void AnAllpropWhoseIncludeNamesCalendarData_IsHonoured()
    {
        var body = new XDocument(new XElement(DavXml.CalDav + "calendar-multiget",
            new XElement(DavXml.Dav + "allprop"),
            new XElement(DavXml.Dav + "include", Expanded("20260901T000000Z", "20261001T000000Z"))));

        var asked = CalendarDataRequest.Asked(body);

        Assert.NotNull(asked);
        Assert.True(asked.Expands);
    }

    private static XDocument Body(params XElement[] properties) =>
        new(new XElement(DavXml.CalDav + "calendar-multiget", new XElement(DavXml.Prop, properties)));

    [Theory]
    [InlineData("20260101T000000Z", "20310101T000000Z", true)]
    [InlineData("20260101T000000Z", "20360101T000000Z", false)]
    [InlineData("00010101T000000Z", "99991231T235959Z", false)]
    public void AWindowWiderThanTheEngineWalks_IsRefused(string start, string end, bool accepted)
    {
        // max-instances cannot hold this line: the cap grows with the window, so a daily series
        // over two centuries stays far under it and still writes ten megabytes per member — five
        // thousand of them per multiget. MaxIcsBytes bounds what comes in; this bounds what goes out.
        var body = Body(Expanded(start, end));

        if (accepted)
        {
            Assert.True(CalendarDataRequest.Asked(body)!.Expands);
            return;
        }

        Assert.Equal(CalDavError.ValidFilter,
            Assert.Throws<DavPreconditionException>(() => CalendarDataRequest.Asked(body)).Condition);
    }

    private static XElement Expanded(string? start, string? end)
    {
        var expand = new XElement(DavXml.CalDav + "expand");
        if (start is not null) expand.Add(new XAttribute("start", start));
        if (end is not null) expand.Add(new XAttribute("end", end));
        return new XElement(CalendarData, expand);
    }

    private static XElement Typed(string? contentType, string? version)
    {
        var element = new XElement(CalendarData);
        if (contentType is not null) element.Add(new XAttribute("content-type", contentType));
        if (version is not null) element.Add(new XAttribute("version", version));
        return element;
    }
}
