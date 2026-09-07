using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

public sealed class CalDavOutcomeTranslatorTests
{
    /// <summary>Every code this slice decided to answer, and nothing else.</summary>
    private static readonly int[] KnownCodes = [201, 204, 403, 404, 412, 503, 507];

    [Theory]
    [InlineData(DavWriteStatus.Created, 201)]
    [InlineData(DavWriteStatus.Replaced, 204)]
    [InlineData(DavWriteStatus.Deleted, 204)]
    [InlineData(DavWriteStatus.NotFound, 404)]
    [InlineData(DavWriteStatus.InvalidCard, 403)]
    [InlineData(DavWriteStatus.UnsupportedVersion, 403)]
    [InlineData(DavWriteStatus.UnsupportedComponent, 403)]
    [InlineData(DavWriteStatus.TooManyInstances, 403)]
    [InlineData(DavWriteStatus.UidConflict, 403)]
    [InlineData(DavWriteStatus.TooLarge, 403)]
    [InlineData(DavWriteStatus.AlreadyExists, 412)]
    [InlineData(DavWriteStatus.PreconditionFailed, 412)]
    [InlineData(DavWriteStatus.CollectionFull, 507)]
    [InlineData(DavWriteStatus.Busy, 503)]
    public async Task EveryStatus_HasItsCode(DavWriteStatus status, int expected)
    {
        var context = NewContext();

        await CalDavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(DavWriteStatus.InvalidCard, "valid-calendar-data")]
    [InlineData(DavWriteStatus.UnsupportedVersion, "supported-calendar-data")]
    [InlineData(DavWriteStatus.UnsupportedComponent, "supported-calendar-component")]
    [InlineData(DavWriteStatus.TooManyInstances, "max-instances")]
    [InlineData(DavWriteStatus.UidConflict, "no-uid-conflict")]
    [InlineData(DavWriteStatus.TooLarge, "max-resource-size")]
    public async Task EveryRefusal_NamesItsCondition(DavWriteStatus status, string condition)
    {
        var context = NewContext();

        await CalDavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        // The namespace is asserted with the name: a max-resource-size in the CardDAV namespace
        // says nothing to a calendar client.
        Assert.Equal(DavXml.CalDav + condition, ConditionOf(context.Response));
    }

    [Theory]
    [InlineData(IcsPrecondition.ValidCalendarData, "valid-calendar-data")]
    [InlineData(IcsPrecondition.ValidCalendarObjectResource, "valid-calendar-object-resource")]
    public async Task AnInvalidFile_NamesThePreconditionTheGateJudged(
        IcsPrecondition judged, string condition)
    {
        var context = NewContext();
        var outcome = new DavWriteOutcome(DavWriteStatus.InvalidCard, null, null, 0, judged);

        await CalDavOutcomeTranslator.WriteAsync(context.Response, outcome, CancellationToken.None);

        // Three preconditions share one status; the outcome carries which one was judged, so the
        // answer names it without reading the file a second time.
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal(DavXml.CalDav + condition, ConditionOf(context.Response));
    }

    [Theory]
    [InlineData(DavWriteStatus.Created)]
    [InlineData(DavWriteStatus.Replaced)]
    [InlineData(DavWriteStatus.Deleted)]
    [InlineData(DavWriteStatus.NotFound)]
    [InlineData(DavWriteStatus.AlreadyExists)]
    [InlineData(DavWriteStatus.PreconditionFailed)]
    [InlineData(DavWriteStatus.CollectionFull)]
    [InlineData(DavWriteStatus.Busy)]
    public async Task WhatNamesNoCondition_WritesNoBodyAtAll(DavWriteStatus status)
    {
        var context = NewContext();

        await CalDavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        Assert.Equal(string.Empty, ReadBody(context.Response));
        Assert.Null(CalDavOutcomeTranslator.ConditionOf(Outcome(status)));
    }

    [Fact]
    public async Task AUidConflict_CarriesTheHrefOfTheHolder()
    {
        var context = NewContext();
        var outcome = new DavWriteOutcome(
            DavWriteStatus.UidConflict, null, "/dav/calendars/x/work/b.ics", 0);

        await CalDavOutcomeTranslator.WriteAsync(context.Response, outcome, CancellationToken.None);

        var condition = XDocument.Parse(ReadBody(context.Response)).Root!
            .Element(DavXml.CalDav + "no-uid-conflict")!;
        Assert.Equal("/dav/calendars/x/work/b.ics", condition.Element(DavXml.Href)!.Value);
    }

    [Fact]
    public async Task Busy_CarriesARetryAfter()
    {
        var context = NewContext();

        await CalDavOutcomeTranslator.WriteAsync(context.Response, Outcome(DavWriteStatus.Busy),
            CancellationToken.None);

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData(DavWriteStatus.Created)]
    [InlineData(DavWriteStatus.Replaced)]
    public async Task AnAcceptedPut_CarriesTheDavHeaderAndItsEtag(DavWriteStatus status)
    {
        var context = NewContext();

        await CalDavOutcomeTranslator.WriteAsync(context.Response,
            new DavWriteOutcome(status, "\"abc\"", null, 3), CancellationToken.None);

        // Always on a calendar: the file is stored verbatim, so the tag is the sent bytes' own.
        Assert.Equal(DavHeaders.ComplianceClasses, context.Response.Headers["DAV"].ToString());
        Assert.Equal("\"abc\"", context.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task ARefusal_CarriesNoDavHeaderAndNoEtag()
    {
        var context = NewContext();

        await CalDavOutcomeTranslator.WriteAsync(context.Response,
            new DavWriteOutcome(DavWriteStatus.TooLarge, "\"abc\"", null, 0), CancellationToken.None);

        Assert.Equal(string.Empty, context.Response.Headers.ETag.ToString());
        Assert.Equal(string.Empty, context.Response.Headers["DAV"].ToString());
    }

    [Fact]
    public void NoTwoStatuses_ShareTheirWholeAnswer()
    {
        // The pair (code, condition) is the answer a client branches on. Two collisions are
        // intended: a replacement and a deletion both say "done, nothing to read" (204), and both
        // race losers say "your condition is false now, re-read" (412).
        var answers = Enum.GetValues<DavWriteStatus>()
            .Select(status => (Status: status, Code: DavWriteAnswer.StatusCodeOf(status),
                Condition: CalDavOutcomeTranslator.ConditionOf(Outcome(status))))
            .ToArray();

        var collisions = answers
            .GroupBy(a => (a.Code, a.Condition))
            .Where(g => g.Count() > 1)
            .Select(g => string.Join('+', g.Select(a => a.Status)))
            .ToArray();

        Assert.Equal(["Replaced+Deleted", "AlreadyExists+PreconditionFailed"], collisions);
    }

    [Fact]
    public async Task EveryEnumValue_IsHandled()
    {
        foreach (var status in Enum.GetValues<DavWriteStatus>())
        {
            var context = NewContext();

            var exception = await Record.ExceptionAsync(() =>
                CalDavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None));

            Assert.Null(exception);
            Assert.Contains(context.Response.StatusCode, KnownCodes);
        }
    }

    [Fact]
    public void EveryEnumValue_IsPinnedToItsOwnCodeByName()
    {
        var pinned = typeof(CalDavOutcomeTranslatorTests)
            .GetMethod(nameof(EveryStatus_HasItsCode))!
            .GetCustomAttributes<InlineDataAttribute>()
            .SelectMany(attribute => attribute.GetData(null!))
            .Select(row => (DavWriteStatus)row[0]!)
            .ToHashSet();

        Assert.Equal(Enum.GetValues<DavWriteStatus>().ToHashSet(), pinned);
    }

    private static DefaultHttpContext NewContext() =>
        new() { Response = { Body = new MemoryStream() } };

    private static DavWriteOutcome Outcome(DavWriteStatus status) => new(status, null, null, 0);

    private static string ReadBody(HttpResponse response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        return reader.ReadToEnd();
    }

    private static XName ConditionOf(HttpResponse response) =>
        Assert.Single(XDocument.Parse(ReadBody(response)).Root!.Elements()).Name;
}
