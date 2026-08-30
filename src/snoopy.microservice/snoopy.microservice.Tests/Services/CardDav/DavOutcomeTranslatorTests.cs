using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavOutcomeTranslatorTests
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
    [InlineData(DavWriteStatus.UidConflict, 403)]
    [InlineData(DavWriteStatus.TooLarge, 403)]
    [InlineData(DavWriteStatus.AlreadyExists, 412)]
    [InlineData(DavWriteStatus.PreconditionFailed, 412)]
    [InlineData(DavWriteStatus.BookFull, 507)]
    [InlineData(DavWriteStatus.Busy, 503)]
    public async Task EveryStatus_HasItsCode(DavWriteStatus status, int expected)
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(DavWriteStatus.InvalidCard, "valid-address-data")]
    [InlineData(DavWriteStatus.UnsupportedVersion, "supported-address-data")]
    [InlineData(DavWriteStatus.UidConflict, "no-uid-conflict")]
    [InlineData(DavWriteStatus.TooLarge, "max-resource-size")]
    public async Task EveryRefusal_NamesItsCondition(DavWriteStatus status, string condition)
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        // A client loops on these refusals whatever the code — DAVx5 catches neither a 403 outside
        // need-privileges nor a 507 — but the named condition makes it a readable log line, where a
        // 500 is an accident indistinguishable server-side. The namespace is asserted with the
        // name: two conditions of the same local name in DAV: and in CardDAV say different things.
        Assert.Equal(DavXml.CardDav + condition, ConditionOf(context.Response));
    }

    [Theory]
    [InlineData(DavWriteStatus.Created)]
    [InlineData(DavWriteStatus.Replaced)]
    [InlineData(DavWriteStatus.Deleted)]
    [InlineData(DavWriteStatus.NotFound)]
    [InlineData(DavWriteStatus.AlreadyExists)]
    [InlineData(DavWriteStatus.PreconditionFailed)]
    [InlineData(DavWriteStatus.BookFull)]
    [InlineData(DavWriteStatus.Busy)]
    public async Task WhatNamesNoCondition_WritesNoBodyAtAll(DavWriteStatus status)
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        // The counterpart of the theory above: an error document invented for a 201 or a 507 would
        // name a precondition the RFC does not define there, and DAVx5 reads it anyway.
        Assert.Equal(string.Empty, ReadBody(context.Response));
        Assert.Null(DavOutcomeTranslator.ConditionOf(status));
    }

    [Fact]
    public async Task AUidConflict_CarriesTheHrefOfTheHolder()
    {
        var context = NewContext();
        var outcome = new DavWriteOutcome(
            DavWriteStatus.UidConflict, null, "/dav/addressbooks/x/default/b.vcf", 0);

        await DavOutcomeTranslator.WriteAsync(context.Response, outcome, CancellationToken.None);

        // Without the href the client knows it lost the UID but not to whom, so it cannot re-read.
        var condition = XDocument.Parse(ReadBody(context.Response)).Root!
            .Element(DavXml.CardDav + "no-uid-conflict")!;
        Assert.Equal("/dav/addressbooks/x/default/b.vcf",
            condition.Element(DavXml.Href)!.Value);
    }

    [Fact]
    public async Task Busy_CarriesARetryAfter()
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(DavWriteStatus.Busy),
            CancellationToken.None);

        // The ONE case where retrying is the right conduct, which is exactly why it carries a code
        // that says so and a header that dates it.
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData(DavWriteStatus.BookFull)]
    [InlineData(DavWriteStatus.NotFound)]
    [InlineData(DavWriteStatus.InvalidCard)]
    [InlineData(DavWriteStatus.Created)]
    public async Task NothingButBusy_CarriesARetryAfter(DavWriteStatus status)
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        // Retry-After on a refusal that will not change invites the very loop the 503 exists to
        // bound: a full book is a state, not a moment.
        Assert.Equal(string.Empty, context.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData(DavWriteStatus.Created)]
    [InlineData(DavWriteStatus.Replaced)]
    [InlineData(DavWriteStatus.Deleted)]
    public async Task AnAcceptedWrite_CarriesTheDavHeaderAndItsEtag(DavWriteStatus status)
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response,
            new DavWriteOutcome(status, "\"abc\"", null, 3), CancellationToken.None);

        // Apple's clients read the compliance classes off whichever response they already have.
        Assert.Equal(DavHeaders.ComplianceClasses, context.Response.Headers["DAV"].ToString());
        Assert.Equal("\"abc\"", context.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task ANullEtag_StaysAbsentRatherThanEmpty()
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(DavWriteStatus.Created),
            CancellationToken.None);

        // The stored bytes differ from the sent ones (a stamped UID): the RFC requires NO ETag, and
        // an empty one would have the client believe it holds the card it sent.
        Assert.Equal(string.Empty, context.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task ARefusal_CarriesNoDavHeaderAndNoEtag()
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response,
            new DavWriteOutcome(DavWriteStatus.TooLarge, "\"abc\"", null, 0), CancellationToken.None);

        Assert.Equal(string.Empty, context.Response.Headers.ETag.ToString());
        Assert.Equal(string.Empty, context.Response.Headers["DAV"].ToString());
    }

    [Fact]
    public void NoTwoStatuses_ShareTheirWholeAnswer()
    {
        // What a swapped arm would slip past a per-status assertion: the pair (code, condition) is
        // the answer a client branches on, and two statuses giving the identical one erase the
        // difference between abandoning a card and re-exporting it. Two collisions are intended:
        // a replacement and a deletion both say "done, nothing to read" (204), and both race
        // losers say "your condition is false now, re-read" (412) — RFC 7232 gives them one code.
        var answers = Enum.GetValues<DavWriteStatus>()
            .Select(status => (Status: status, Code: DavOutcomeTranslator.StatusCodeOf(status),
                Condition: DavOutcomeTranslator.ConditionOf(status)))
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
        // The assertion that makes the class stay honest: a status added later without a branch
        // would otherwise fall through to whatever the default does — and a default here is a 500.
        foreach (var status in Enum.GetValues<DavWriteStatus>())
        {
            var context = NewContext();

            var exception = await Record.ExceptionAsync(() =>
                DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None));

            Assert.Null(exception);
            Assert.NotEqual(500, context.Response.StatusCode);
            // "Not 500" alone passes against a 0, a 200 or anything invented: the code must be one
            // this slice actually decided to answer.
            Assert.Contains(context.Response.StatusCode, KnownCodes);
        }
    }

    [Fact]
    public void EveryEnumValue_IsPinnedToItsOwnCodeByName()
    {
        // Guards the theory above against the vacuity it invites: a status added to the enum and
        // handled by some catch-all arm would satisfy EveryEnumValue_IsHandled while nothing ever
        // said what code it earns. Here the InlineData set itself must name every member.
        var pinned = typeof(DavOutcomeTranslatorTests)
            .GetMethod(nameof(EveryStatus_HasItsCode))!
            .GetCustomAttributes<InlineDataAttribute>()
            .SelectMany(attribute => attribute.GetData(null!))
            .Select(row => (DavWriteStatus)row[0]!)
            .ToHashSet();

        Assert.Equal(Enum.GetValues<DavWriteStatus>().ToHashSet(), pinned);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public void InnoDbSayingComeBackLater_IsTransient(int mySqlErrorNumber) =>
        Assert.True(DavOutcomeTranslator.IsTransient(MySqlErrors.With(mySqlErrorNumber)));

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public void InnoDbSayingComeBackLater_IsTransientThroughEfsWrapper(int mySqlErrorNumber) =>
        // EF wraps whatever the provider threw during a save, so the number never sits on the
        // exception the writer catches; reading only the outer one would answer 500 on every
        // lock wait that actually happens in production.
        Assert.True(DavOutcomeTranslator.IsTransient(
            new DbUpdateException("save failed", MySqlErrors.With(mySqlErrorNumber))));

    [Theory]
    [InlineData(1062)] // duplicate entry — the unique index, a refusal to translate, not to retry
    [InlineData(1146)] // no such table — a fault, and retrying it forever helps nobody
    public void AnyOtherMySqlNumber_IsNotTransient(int mySqlErrorNumber) =>
        Assert.False(DavOutcomeTranslator.IsTransient(MySqlErrors.With(mySqlErrorNumber)));

    [Fact]
    public void AnOrdinaryDbUpdateException_IsNotTransient() =>
        // The unique-index race arrives as exactly this and is replayed, not retried later.
        Assert.False(DavOutcomeTranslator.IsTransient(new DbUpdateException("Duplicate entry")));

    [Fact]
    public void AnythingElse_IsNotTransient() =>
        Assert.False(DavOutcomeTranslator.IsTransient(new InvalidOperationException()));

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public void TheFabricatedException_ReallyCarriesItsNumber(int number) =>
        // Without this the transient tests would pass over an exception carrying nothing, since
        // the reflection that builds one is the only thing standing in for an engine we cannot run.
        Assert.Equal(number, MySqlErrors.With(number).Number);

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
