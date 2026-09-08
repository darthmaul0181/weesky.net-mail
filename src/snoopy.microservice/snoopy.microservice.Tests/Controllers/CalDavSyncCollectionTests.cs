using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>sync-collection per calendar: 4c's scenarios keyed by <c>calendar_id</c>, and what two
/// calendars of one account owe each other — nothing.</summary>
public sealed class CalDavSyncCollectionTests : IAsyncLifetime
{
    private static readonly Guid Epoch = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherEpoch = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly Mock<ILogger<CalDavController>> logger = new();

    private DavTestServer server = null!;
    private Guid work;
    private Guid other;
    private ulong counter;
    private bool counterPinned;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(
            overrides: services => services.AddSingleton(logger.Object));
        work = GivenCalendar("work");
        other = GivenCalendar("other");
        // Both state rows exist with a KNOWN epoch before any request, so TokenAt(n) is readable.
        UpsertState(work, Epoch, seq: 0);
        UpsertState(other, OtherEpoch, seq: 0);
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task AnInitialSync_AnswersTheWholeCalendarAndNoTombstone()
    {
        GivenEvents("a.ics", "b.ics");
        GivenATombstone("gone.ics", rank: 3);

        var response = await Report(Calendar(), SyncBody(token: null));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Empty(ResponsesOfStatus(response, 404));
    }

    [Fact]
    public async Task AnIncrementalSync_AnswersOnlyWhatMovedSince_UnderTheHrefAClientWillFetch()
    {
        GivenEventAtRank("a.ics", 5);
        GivenEventAtRank("b.ics", 12);

        var response = await Report(Calendar(), SyncBody(TokenAt(8)));

        Assert.Equal([DavPaths.Event(UserId, "work", "b.ics")], HrefsOf(response));
    }

    [Fact]
    public async Task AnEventAtExactlyTheTokenRank_IsNotReServed()
    {
        GivenEventAtRank("seen.ics", 8);
        GivenEventAtRank("fresh.ics", 12);

        var response = await Report(Calendar(), SyncBody(TokenAt(8)));

        Assert.Single(HrefsOf(response), h => h.EndsWith("fresh.ics"));
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("seen.ics"));
    }

    [Fact]
    public async Task ATombstoneInTheWindow_ComesBackAs404_AStatusDirectlyUnderItsResponse()
    {
        GivenATombstone("gone.ics", rank: 12);

        var response = await Report(Calendar(), SyncBody(TokenAt(8)));

        var gone = ResponsesOfStatus(response, 404).Single();
        Assert.Equal(DavPaths.Event(UserId, "work", "gone.ics"), gone.Element(DavXml.Href)!.Value);
        Assert.Single(gone.Elements(DavXml.Status));
        Assert.Empty(gone.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task ATombstoneAtExactlyTheTokenRank_IsNotReServed()
    {
        GivenATombstone("old.ics", rank: 8);
        GivenATombstone("gone.ics", rank: 12);

        var response = await Report(Calendar(), SyncBody(TokenAt(8)));

        Assert.EndsWith("gone.ics", ResponsesOfStatus(response, 404).Single().Element(DavXml.Href)!.Value);
    }

    [Fact]
    public async Task ATombstonePastTheCounter_IsNotServedUnderTheReturnedToken()
    {
        GivenTheCounterAt(20);
        GivenATombstone("late.ics", rank: 25);

        var response = await Report(Calendar(), SyncBody(TokenAt(8)));

        Assert.Empty(ResponsesOfStatus(response, 404));
    }

    [Fact]
    public async Task ItServesThePropertiesTheRequestAsked()
    {
        GivenEventAtRank("a.ics", 12);

        var response = await Report(Calendar(), SyncBody(TokenAt(8), props: ["getetag", "resourcetype"]));

        var served = ResponseOf(response, "a.ics");
        Assert.Equal("\"hash-of-a.ics\"", served.Descendants(DavXml.Dav + "getetag").Single().Value);
        Assert.Single(served.Descendants(DavXml.Dav + "resourcetype"));
    }

    [Fact]
    public async Task CalendarDataInASyncCollection_IsServedAsStored()
    {
        GivenEventAtRank("a.ics", 12);

        var response = await Report(Calendar(), SyncBody(TokenAt(8), caldavProps: ["calendar-data"]));

        // The event table holds calendar-data, so a sync-collection naming it reads the file as
        // a PROPFIND would — never expanded, RFC 4791 defining expand for multiget and query alone.
        var served = ResponseOf(response, "a.ics").Descendants(DavXml.CalDav + "calendar-data").Single().Value;
        Assert.Equal(CalDavPutTests.Event("u-a.ics").Split("\r\n"), served.Split('\n'));
    }

    [Fact]
    public async Task AResponseAskedForNoProperty_StillCarriesAStatusOfItsOwn()
    {
        GivenEventAtRank("a.ics", 12);

        var response = await Report(Calendar(), SyncBody(TokenAt(8), props: []));

        var served = ResponseOf(response, "a.ics");
        Assert.Equal("HTTP/1.1 200 OK", served.Elements(DavXml.Status).Single().Value);
        Assert.Empty(served.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task TheNewTokenIsTheCounterReadBeforeTheRows()
    {
        GivenEventAtRank("a.ics", 12);
        GivenTheCounterAt(20);

        var response = await Report(Calendar(), SyncBody(TokenAt(8)));

        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 20, 0)), NewTokenOf(response));
    }

    [Fact]
    public async Task ACounterAdvancedWhileTheRowsAreRead_DoesNotReachTheReturnedToken()
    {
        // InMemory has no transactions, so no snapshot pins the read ORDER — this reader can: it
        // advances the counter the moment the rows are enumerated.
        await using var racing = await DavTestServer.StartAsync(
            overrides: services => services.AddScoped<IDavCalendarReader, CounterBumpingReader>());
        Guid calendarId;
        using (var db = racing.CreateContext())
        {
            var row = NewCalendar(racing.UserId, "work");
            calendarId = row.Id;
            db.Calendars.Add(row);
            db.CalendarSyncStates.Add(new CalendarSyncState
            {
                CalendarId = calendarId, Epoch = Epoch, Seq = 20, PrunedBelow = 0,
            });
            SeedEvent(db, racing.UserId, calendarId, "a.ics", 12);
            db.SaveChanges();
        }

        var response = await racing.SendAsync(
            "REPORT", DavPaths.Calendar(racing.UserId, "work"), BuildSyncBody(TokenAt(8)));

        Assert.Equal(207, response.StatusCode);
        Assert.Single(HrefsOf(response), h => h.EndsWith("a.ics"));
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 20, 0)), NewTokenOf(response));
    }

    [Fact]
    public async Task ARowWrittenAfterTheCounterWasRead_IsNotServedUnderTheReturnedToken()
    {
        GivenTheCounterAt(20);
        GivenEventAtRank("late.ics", 25);

        var response = await Report(Calendar(), SyncBody(TokenAt(8)));

        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("late.ics"));
    }

    [Fact]
    public async Task ARefusedToken_Answers403ValidSyncToken()
    {
        var response = await Report(Calendar(), SyncBody(TokenAt(2), pruned: 10));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(CalDavError.ValidSyncToken, ConditionOf(response));
    }

    [Fact]
    public async Task ATokenOfAnotherCalendar_Is403ValidSyncToken()
    {
        GivenEventAtRank("a.ics", 12);
        GivenEventAtRank(other, "c.ics", 12);

        // The other calendar's own token, at a rank it holds: only the epoch tells them apart,
        // and a token read across calendars would replay one collection's history onto another.
        var response = await Report(Calendar(),
            SyncBody(DavSyncToken.Token(new SyncState(OtherEpoch, 8, 0))));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(CalDavError.ValidSyncToken, ConditionOf(response));
    }

    [Fact]
    public async Task TwoCalendarsOfOneAccount_MoveIndependently()
    {
        GivenEventAtRank(other, "c.ics", 3);
        var before = await CtagOf("other");
        var otherToken = DavSyncToken.Token(new SyncState(OtherEpoch, 3, 0));

        var put = await server.SendAsync("PUT", DavPaths.Event(UserId, "work", "new.ics"),
            CalDavPutTests.Event("fresh"));
        Assert.Equal(201, put.StatusCode);

        // A write in `work` is `work`'s rank and `work`'s ctag: `other` has nothing to tell its
        // clients, and a counter shared across calendars would wake every phone on every write.
        Assert.Equal(before, await CtagOf("other"));
        var response = await Report(DavPaths.Calendar(UserId, "other"), BuildSyncBody(otherToken));
        Assert.Equal(207, response.StatusCode);
        Assert.Empty(ResponsesOf(response));
        Assert.Equal(otherToken, NewTokenOf(response));
        Assert.Single(HrefsOf(await Report(Calendar(), SyncBody(token: null))), h => h.EndsWith("new.ics"));
    }

    [Fact]
    public async Task ACalendarWithoutItsStateRow_Is403ValidSyncToken_AndAnErrorInTheLog()
    {
        GivenCalendar("bare");

        var response = await Report(DavPaths.Calendar(UserId, "bare"), BuildSyncBody(null));

        // A calendar is born with its state row (décision 2 of 5a): one without is a base that
        // lost a row, refused rather than repaired in passing, and said out loud.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(CalDavError.ValidSyncToken, ConditionOf(response));
        logger.VerifyErrorLoggedContaining("bare");
    }

    [Fact]
    public async Task ATruncatedCut_LandsOnTheFirstRankWhoseTokenReadsBack()
    {
        GivenEventAtRank("a.ics", 10);
        GivenEventAtRank("b.ics", 50);
        GivenEventAtRank("c.ics", 55);
        GivenEventAtRank("d.ics", 56);
        GivenTheCounterAt(60);
        GivenPrunedBelow(50);

        var response = await Report(Calendar(), SyncBody(token: null, limit: 1));

        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Single(ResponsesOfStatus(response, 507));
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 50, 0)), NewTokenOf(response));
    }

    [Theory]
    [InlineData(true, null, 50ul)]
    [InlineData(true, 1, 50ul)]
    [InlineData(false, 1, 50ul)]
    [InlineData(true, null, 60ul)]
    [InlineData(true, 1, 60ul)]
    [InlineData(false, null, 60ul)]
    public async Task EveryEmittedToken_IsOneTheServerAcceptsBack(bool initial, int? limit, ulong watermark)
    {
        GivenEventAtRank("a.ics", 10);
        GivenEventAtRank("b.ics", 50);
        GivenEventAtRank("c.ics", 55);
        GivenEventAtRank("d.ics", 56);
        GivenATombstone("gone.ics", 58);
        GivenTheCounterAt(60);
        GivenPrunedBelow(watermark);

        var response = await Report(Calendar(), SyncBody(initial ? null : TokenAt(watermark), limit: limit));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(SyncTokenKind.Sequence,
            ReadBack(NewTokenOf(response), new SyncState(Epoch, 60, watermark)));
    }

    [Fact]
    public async Task TheSyncTokenAPropfindEmits_IsOneTheReportAcceptsBack()
    {
        GivenEventAtRank("a.ics", 10);
        GivenTheCounterAt(60);
        GivenPrunedBelow(60);

        var propfind = await server.PropfindAsync(Calendar(), "0",
            new XDocument(new XElement(DavXml.Dav + "propfind",
                new XElement(DavXml.Prop, new XElement(DavXml.Dav + "sync-token")))).ToString());
        var emitted = XDocument.Parse(propfind.Body).Descendants(DavXml.Dav + "sync-token").Single().Value;

        var response = await Report(Calendar(), BuildSyncBody(emitted));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(emitted, NewTokenOf(response));
    }

    [Fact]
    public async Task ALimit_TruncatesOnARankBoundaryAndSaysSo()
    {
        GivenEventsAtRank(rank: 10, count: 3);
        GivenEventsAtRank(rank: 11, count: 3);

        var response = await Report(Calendar(), SyncBody(TokenAt(0), limit: 4));

        Assert.Equal(3, ResponsesOfStatus(response, 200).Count);
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 10, 0)), NewTokenOf(response));
        Assert.Single(ResponsesOfStatus(response, 507));
    }

    [Fact]
    public async Task ASingleRankBiggerThanTheLimit_IsServedWhole()
    {
        GivenEventsAtRank(rank: 10, count: 5);

        var response = await Report(Calendar(), SyncBody(TokenAt(0), limit: 2));

        Assert.Equal(5, ResponsesOfStatus(response, 200).Count);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("infinite")]
    public async Task AValidSyncLevel_IsAccepted(string level) =>
        Assert.Equal(207, (await Report(Calendar(), SyncBody(TokenAt(0), syncLevel: level))).StatusCode);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("infinity")]
    public async Task AnAbsentSyncLevel_FallsBackOnAnyDepthHeader(string depth)
    {
        var response = await Report(Calendar(), SyncBody(TokenAt(0), syncLevel: null), depth: depth);

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task AnAbsentSyncLevelAndNoDepthAtAll_Answers400()
    {
        var response = await Report(Calendar(), SyncBody(TokenAt(0), syncLevel: null), depth: null);

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ASyncLevelOfAnotherValue_Answers400()
    {
        var response = await Report(Calendar(), SyncBody(TokenAt(0), syncLevel: "2"));

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ASyncCollectionOnTheHome_IsARefusal()
    {
        var response = await Report(DavPaths.CalendarHome(UserId), BuildSyncBody(null));

        // The home announces expand-property alone; a sync of "every calendar" is a report the
        // collection never claimed.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task ASyncCollectionOnAnEvent_IsARefusal()
    {
        GivenEventAtRank("a.ics", 12);

        var response = await Report(DavPaths.Event(UserId, "work", "a.ics"), BuildSyncBody(null));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    private Task<DavTestResponse> Report(string path, string? body, string? depth = null) =>
        server.SendAsync("REPORT", path, body, depth);

    private string Calendar() => DavPaths.Calendar(UserId, "work");

    private async Task<string> CtagOf(string calendarName)
    {
        var response = await server.PropfindAsync(DavPaths.Calendar(UserId, calendarName), "0",
            new XDocument(new XElement(DavXml.Dav + "propfind",
                new XElement(DavXml.Prop, new XElement(DavXml.CalendarServer + "getctag")))).ToString());
        return XDocument.Parse(response.Body).Descendants(DavXml.CalendarServer + "getctag").Single().Value;
    }

    private static string TokenAt(ulong sequence) => DavSyncToken.Token(new SyncState(Epoch, sequence, 0));

    private static SyncTokenKind ReadBack(string token, SyncState state) =>
        DavSyncToken.Read(new XElement(DavXml.Dav + "sync-token", token), state).Kind;

    private Guid GivenCalendar(string davName)
    {
        using var db = server.CreateContext();
        var row = NewCalendar(UserId, davName);
        db.Calendars.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static CalendarRow NewCalendar(Guid owner, string davName) => new()
    {
        Id = Guid.NewGuid(), UserId = owner, DavName = davName, DisplayName = davName,
        Description = string.Empty, Color = "#336699", Order = 1, TimeZone = Ics.Zone, IsVisible = true,
    };

    private void GivenEvents(params string[] names)
    {
        foreach (var name in names) GivenEventAtRank(name, counter + 1);
    }

    private void GivenEventAtRank(string davName, ulong rank) => GivenEventAtRank(work, davName, rank);

    private void GivenEventAtRank(Guid calendarId, string davName, ulong rank)
    {
        using var db = server.CreateContext();
        SeedEvent(db, UserId, calendarId, davName, rank);
        db.SaveChanges();
        if (calendarId == work) RaiseCounterTo(rank);
        else UpsertState(calendarId, OtherEpoch, rank);
    }

    private List<string> GivenEventsAtRank(ulong rank, int count)
    {
        List<string> names = [.. Enumerable.Range(0, count).Select(_ => $"{Guid.NewGuid():N}.ics")];
        foreach (var name in names) GivenEventAtRank(name, rank);
        return names;
    }

    private void GivenATombstone(string davName, ulong rank)
    {
        using var db = server.CreateContext();
        db.CalendarTombstones.Add(new CalendarTombstone
        {
            CalendarId = work, DavName = davName, SyncSequence = rank, DeletedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        RaiseCounterTo(rank);
    }

    private void GivenTheCounterAt(ulong seq)
    {
        counterPinned = true;
        counter = seq;
        UpsertState(work, Epoch, seq);
    }

    private void GivenPrunedBelow(ulong watermark) => UpsertState(work, Epoch, counter, watermark);

    private void RaiseCounterTo(ulong rank)
    {
        if (counterPinned || rank <= counter) return;
        counter = rank;
        UpsertState(work, Epoch, rank);
    }

    private void UpsertState(Guid calendarId, Guid epoch, ulong seq, ulong? prunedBelow = null)
    {
        using var db = server.CreateContext();
        var row = db.CalendarSyncStates.SingleOrDefault(s => s.CalendarId == calendarId);
        if (row is null)
        {
            db.CalendarSyncStates.Add(new CalendarSyncState
            {
                CalendarId = calendarId, Epoch = epoch, Seq = seq, PrunedBelow = prunedBelow ?? 0,
            });
        }
        else
        {
            row.Seq = seq;
            if (prunedBelow is { } watermark) row.PrunedBelow = watermark;
        }

        db.SaveChanges();
    }

    private static void SeedEvent(PreferencesTestDbContext db, Guid owner, Guid calendarId,
        string davName, ulong rank) =>
        db.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(),
            CalendarId = calendarId,
            UserId = owner,
            Uid = $"u-{davName}",
            DavName = davName,
            StartsAt = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            EndsAt = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc),
            FirstOccurrence = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            LastOccurrence = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc),
            Transparency = "OPAQUE",
            IcsRaw = CalDavPutTests.Event($"u-{davName}"),
            IcsHash = $"hash-of-{davName}",
            SyncSequence = rank,
            UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
        });

    /// <summary>Raises the pruning watermark before building the body carrying the doomed token.</summary>
    private string SyncBody(string? token, string[]? props = null, string[]? caldavProps = null,
        int? limit = null, string? syncLevel = "1", ulong? pruned = null)
    {
        if (pruned is { } watermark) UpsertState(work, Epoch, Math.Max(counter, watermark), watermark);
        return BuildSyncBody(token, props, caldavProps, limit, syncLevel);
    }

    private static string BuildSyncBody(string? token, string[]? props = null,
        string[]? caldavProps = null, int? limit = null, string? syncLevel = "1")
    {
        var root = new XElement(DavXml.Dav + "sync-collection",
            new XElement(DavXml.Dav + "sync-token", token ?? string.Empty));
        if (syncLevel is not null) root.Add(new XElement(DavXml.Dav + "sync-level", syncLevel));
        if (limit is { } bound)
            root.Add(new XElement(DavXml.Dav + "limit", new XElement(DavXml.Dav + "nresults", bound)));

        var prop = new XElement(DavXml.Prop);
        foreach (var name in props ?? ["getetag"]) prop.Add(new XElement(DavXml.Dav + name));
        foreach (var name in caldavProps ?? []) prop.Add(new XElement(DavXml.CalDav + name));
        root.Add(prop);

        return new XDocument(root).ToString();
    }

    private static XName ConditionOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Elements().Single().Name;

    private static string NewTokenOf(DavTestResponse response) =>
        XDocument.Parse(response.Body).Root!.Element(DavXml.Dav + "sync-token")!.Value;

    private static List<XElement> ResponsesOf(DavTestResponse response) =>
        [.. XDocument.Parse(response.Body).Root!.Elements(DavXml.Response)];

    private static XElement ResponseOf(DavTestResponse response, string davName) =>
        ResponsesOf(response).Single(r => r.Element(DavXml.Href)!.Value.EndsWith(davName, StringComparison.Ordinal));

    private static List<string> HrefsOf(DavTestResponse response) =>
        [.. ResponsesOf(response).Select(r => r.Element(DavXml.Href)!.Value)];

    private static List<XElement> ResponsesOfStatus(DavTestResponse response, int statusCode) =>
        [.. ResponsesOf(response)
            .Where(r => r.Descendants(DavXml.Status).Any(s => s.Value.Contains($" {statusCode} ")))];

    /// <summary>Real reads over the server's own database, but the first enumeration of the
    /// changed rows advances the counter first — the concurrent write the read order tolerates.</summary>
    private sealed class CounterBumpingReader(PreferencesDbContext context) : IDavCalendarReader
    {
        private readonly DavCalendarReader inner = new(context);

        public async IAsyncEnumerable<DavEvent> ChangedAsync(Guid calendarId, ulong after, ulong upTo,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var state = await context.CalendarSyncStates.SingleAsync(
                s => s.CalendarId == calendarId, cancellationToken);
            state.Seq = 99;
            await context.SaveChangesAsync(cancellationToken);

            await foreach (var member in inner.ChangedAsync(calendarId, after, upTo, cancellationToken))
                yield return member;
        }

        public Task<IReadOnlyList<DavCalendar>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.ListAsync(userId, cancellationToken);

        public Task<DavCalendar?> FindCalendarAsync(Guid userId, string calendarName,
            CancellationToken cancellationToken) =>
            inner.FindCalendarAsync(userId, calendarName, cancellationToken);

        public IAsyncEnumerable<DavEvent> StreamAsync(Guid calendarId, ulong upTo,
            CancellationToken cancellationToken) =>
            inner.StreamAsync(calendarId, upTo, cancellationToken);

        public Task<DavEvent?> FindAsync(Guid calendarId, string davName, CancellationToken cancellationToken) =>
            inner.FindAsync(calendarId, davName, cancellationToken);

        public Task<IReadOnlyList<DavEvent>> FindManyAsync(Guid calendarId, IReadOnlyList<string> davNames,
            CancellationToken cancellationToken) =>
            inner.FindManyAsync(calendarId, davNames, cancellationToken);

        public Task<IReadOnlyList<CalendarTombstone>> TombstonesAsync(Guid calendarId, ulong after,
            ulong upTo, CancellationToken cancellationToken) =>
            inner.TombstonesAsync(calendarId, after, upTo, cancellationToken);

        public IAsyncEnumerable<DavEvent> CandidatesAsync(Guid calendarId, DateTime? fromUtc,
            DateTime? toUtc, EventColumnFilter columns, ulong upTo, CancellationToken cancellationToken) =>
            inner.CandidatesAsync(calendarId, fromUtc, toUtc, columns, upTo, cancellationToken);

        public Task<int> CountAsync(Guid calendarId, CancellationToken cancellationToken) =>
            inner.CountAsync(calendarId, cancellationToken);
    }
}
