using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// The rule of the whole tranche on the calendar tree: <b>no refusal of the store and no client
/// input reaches a DAV client as a 500</b>. Each named case asserts the answer it really receives
/// — the status AND the condition — never the mere absence of a 500.
/// </summary>
public sealed class CalDavNoFiveHundredTests : IAsyncLifetime
{
    /// <summary>Every status the surface may answer a request with, the net's whole vocabulary.</summary>
    private static readonly int[] Answerable = [200, 201, 204, 207, 304, 308, 400, 403, 404, 405, 412, 413, 507];

    private DavTestServer server = null!;
    private Guid calendarId;

    private Mock<IDavCalendarWriter> Writer { get; } = new();

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(overrides: services =>
            services.AddScoped<IDavCalendarWriter>(_ => Writer.Object));
        calendarId = GivenCalendar("work");
        DelegateToTheRealWriter();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Theory]
    [InlineData(DavWriteStatus.InvalidCard, 403, "valid-calendar-data")]
    [InlineData(DavWriteStatus.UnsupportedVersion, 403, "supported-calendar-data")]
    [InlineData(DavWriteStatus.UnsupportedComponent, 403, "supported-calendar-component")]
    [InlineData(DavWriteStatus.TooManyInstances, 403, "max-instances")]
    [InlineData(DavWriteStatus.UidConflict, 403, "no-uid-conflict")]
    [InlineData(DavWriteStatus.TooLarge, 403, "max-resource-size")]
    [InlineData(DavWriteStatus.CollectionFull, 507, null)]
    [InlineData(DavWriteStatus.Busy, 503, null)]
    [InlineData(DavWriteStatus.AlreadyExists, 412, null)]
    [InlineData(DavWriteStatus.PreconditionFailed, 412, null)]
    [InlineData(DavWriteStatus.NotFound, 404, null)]
    public async Task EveryStoreRefusalOnPut_ReachesTheClientAsItsOwnNamedAnswer(
        DavWriteStatus status, int expected, string? condition)
    {
        GivenTheWriterAnswers(status);

        var response = await Put(Href("a.ics"), CalDavPutTests.Event("u1"));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(condition is null ? null : DavXml.CalDav + condition, ConditionOrNull(response));
    }

    [Theory]
    [InlineData(DavWriteStatus.Deleted, 204)]
    [InlineData(DavWriteStatus.NotFound, 404)]
    [InlineData(DavWriteStatus.PreconditionFailed, 412)]
    [InlineData(DavWriteStatus.Busy, 503)]
    public async Task EveryOutcomeOfDelete_ReachesTheClientAsItsOwnCode(DavWriteStatus status, int expected)
    {
        await GivenAnEvent("a.ics");
        Writer.Setup(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new DavWriteOutcome(status, null, null, 0));

        var response = await server.SendAsync("DELETE", Href("a.ics"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ARealUidConflict_Answers403WithTheHolderHref()
    {
        await GivenAnEvent("a.ics", "shared");

        var response = await Put(Href("b.ics"), CalDavPutTests.Event("shared"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CalDav + "no-uid-conflict", ConditionOrNull(response));
        Assert.Equal(Href("a.ics"), ErrorRootOf(response).Descendants(DavXml.Href).Single().Value);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ALockWaitTimeout_Answers503AndNot500(int number)
    {
        // End to end over the REAL writer, with only the engine faked: InMemory arbitrates no lock
        // and can raise no MySqlException, so the save throws what MariaDB would have thrown.
        GivenTheStoreWillTimeOutOnItsLock(number, typeof(CalendarEvent), putOnly: true);

        var response = await Put(Href("a.ics"), CalDavPutTests.Event("u1"));

        Assert.Equal(503, response.StatusCode);
        Assert.Equal("1", response.Header("Retry-After"));
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ALockWaitTimeoutWhileArchivingARefusedBody_StillAnswers412(int number)
    {
        // The archive rides a 412 whose precondition has ALREADY failed: the refusal is the answer,
        // the archive a courtesy that may be lost.
        await GivenAnEvent("a.ics");
        GivenTheStoreWillTimeOutOnItsLock(number, typeof(CalendarRevision), putOnly: false);

        var response = await Put(Href("a.ics"), CalDavPutTests.Event("u1", "G"), ifMatch: "\"stale\"");

        Assert.Equal(412, response.StatusCode);
    }

    [Theory]
    [InlineData("20270102T100000", 201)]
    [InlineData("20270102T160000", 403)]
    public async Task AnOverrideWrittenBeforeItsMaster_IsAnsweredNotCrashedOn(string recurrenceId, int expected)
    {
        // The one 500 a conformance pass drew out of the deployed server: RFC 5545 imposes no order
        // between components, and every body this suite carried put the master first.
        var response = await Put(Href("a.ics"), Ics.OverrideBeforeMaster(recurrenceId));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task AFaultingStore_IsTheOnlyThingLeftThatTraverses()
    {
        // The honest counterpart: a fault is NOT transient, must not be dressed as a 503 that has
        // every client retry a broken server for ever, and still traverses. This host runs no
        // exception handler, so it surfaces here as the throw the real pipeline turns into the 500.
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .ThrowsAsync(new DbUpdateException("the table is gone"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Put(Href("a.ics"), CalDavPutTests.Event("u1")));
    }

    [Theory]
    [MemberData(nameof(EveryShapeUnderEveryAbuse))]
    public async Task NoVerbNoBodyAndNoHeader_EverBecomesA500(string shape, string method, string? body,
        string? depth, string? ifMatch)
    {
        // The net over the four shapes: unknown verbs, bodies that are no XML and no calendar,
        // headers that parse as nothing. Each answer is one the surface decided to give.
        var headers = new Dictionary<string, string>();
        if (ifMatch is not null) headers["If-Match"] = ifMatch;
        var path = shape switch
        {
            "collection" => DavPaths.CalendarCollection,
            "home" => DavPaths.CalendarHome(UserId),
            "calendar" => DavPaths.Calendar(UserId, "work"),
            _ => Href("a.ics"),
        };

        var response = await server.SendAsync(method, path, body, depth, headers);

        Assert.Contains(response.StatusCode, Answerable);
    }

    public static TheoryData<string, string, string?, string?, string?> EveryShapeUnderEveryAbuse()
    {
        var data = new TheoryData<string, string, string?, string?, string?>();
        string?[] bodies =
        [
            null, "not xml at all", "<D:propfind xmlns:D=\"DAV:\"><D:prop/>", Garbage(),
            // A calendar-query the parser must judge rather than trip over: without one, the whole
            // of CalendarQueryFilter.Parse is out of this net's reach.
            "<C:calendar-query xmlns:D=\"DAV:\" xmlns:C=\"urn:ietf:params:xml:ns:caldav\"><D:prop>"
            + "<C:calendar-data><C:expand start=\"banana\" end=\"\"/></C:calendar-data></D:prop>"
            + "<C:filter><C:comp-filter name=\"VCALENDAR\"><C:comp-filter name=\"VEVENT\">"
            + "<C:time-range start=\"99991231T235959Z\"/><C:prop-filter name=\"SUMMARY\">"
            + "<C:text-match collation=\"i;nope\">x</C:text-match></C:prop-filter>"
            + "</C:comp-filter></C:comp-filter></C:filter></C:calendar-query>",
            // The free-busy-query too: its time-range is mandatory and its own to parse.
            "<C:free-busy-query xmlns:C=\"urn:ietf:params:xml:ns:caldav\">"
            + "<C:time-range end=\"banana\"/></C:free-busy-query>",
        ];
        foreach (var shape in new[] { "collection", "home", "calendar", "event" })
        foreach (var method in new[] { "PUT", "DELETE", "PROPFIND", "PROPPATCH", "REPORT", "MKCALENDAR", "MKCOL", "BREW", "GET" })
        foreach (var body in bodies)
            data.Add(shape, method, body, "banana", "not-an-etag");
        return data;
    }

    /// <summary>Deterministic bytes no client would send. SendAsync re-encodes them as UTF-8, so the
    /// decoder's own refusal is not what this exercises — ABodyThatIsNotStrictUtf8 covers that one
    /// through PutBytes; here it is the XML reader meeting a body it can make nothing of.</summary>
    private static string Garbage() =>
        Encoding.Latin1.GetString(Enumerable.Range(0, 256).Select(i => (byte)(i * 7 % 256)).ToArray());

    private string Href(string davName) => DavPaths.Event(UserId, "work", davName);

    private async Task GivenAnEvent(string davName, string uid = "u1")
    {
        var response = await Put(Href(davName), CalDavPutTests.Event(uid));
        Assert.Equal(201, response.StatusCode);
    }

    private void GivenTheWriterAnswers(DavWriteStatus status) =>
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .ReturnsAsync(new DavWriteOutcome(status, null,
                status is DavWriteStatus.UidConflict ? Href("b.ics") : null, 0));

    /// <summary>The real writer over a context whose save of <paramref name="on"/> throws what
    /// InnoDB throws after waiting <c>innodb_lock_wait_timeout</c>, wrapped as EF wraps it.</summary>
    private void GivenTheStoreWillTimeOutOnItsLock(int number, Type on, bool putOnly)
    {
        if (putOnly)
        {
            Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(),
                    It.IsAny<RevisionCause>()))
                .Returns((Guid user, Guid calendar, string name, string ics, CancellationToken token,
                        bool createOnly, string? ifMatch, RevisionCause cause) =>
                    WithRealWriter(new LockedOutDbContext(server.DatabaseName, number, on),
                        real => real.PutAsync(user, calendar, name, ics, token, createOnly, ifMatch, cause)));
            return;
        }

        Writer.Setup(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, Guid calendar, string name, string ics, CancellationToken token) =>
                WithRealWriter(new LockedOutDbContext(server.DatabaseName, number, on),
                    real => real.ArchiveRejectedAsync(user, calendar, name, ics, token)));
    }

    private void DelegateToTheRealWriter()
    {
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<RevisionCause>()))
            .Returns((Guid user, Guid calendar, string name, string ics, CancellationToken token,
                    bool createOnly, string? ifMatch, RevisionCause cause) =>
                WithRealWriter(server.CreateContext(),
                    real => real.PutAsync(user, calendar, name, ics, token, createOnly, ifMatch, cause)));
        Writer.Setup(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns((Guid user, Guid calendar, string name, CancellationToken token, string? ifMatch) =>
                WithRealWriter(server.CreateContext(),
                    real => real.DeleteAsync(user, calendar, name, token, ifMatch)));
        Writer.Setup(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, Guid calendar, string name, string ics, CancellationToken token) =>
                WithRealWriter(server.CreateContext(),
                    real => real.ArchiveRejectedAsync(user, calendar, name, ics, token)));
    }

    private static async Task<T> WithRealWriter<T>(
        PreferencesDbContext context, Func<IDavCalendarWriter, Task<T>> call)
    {
        await using (context)
        {
            var sync = new TestCalendarSyncStore(context);
            var store = new CalendarEventStore(context, sync, NullLogger<CalendarEventStore>.Instance);
            return await call(new DavCalendarWriter(store, sync, context, NullLogger<DavCalendarWriter>.Instance));
        }
    }

    private async Task<DavTestResponse> Put(string path, string body, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.TryAddWithoutValidation("Content-Type", "text/calendar");
        request.Content = content;
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        using var response = await server.Client.SendAsync(request);
        return await DavTestResponse.ReadAsync(response);
    }

    private Guid GivenCalendar(string davName)
    {
        using var db = server.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(), UserId = UserId, DavName = davName, DisplayName = "Work",
            Description = string.Empty, Color = "#336699", Order = 1,
            TimeZone = "Europe/Brussels", IsVisible = true,
        };
        db.Calendars.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static XElement ErrorRootOf(DavTestResponse response)
    {
        var root = XDocument.Parse(response.Body).Root!;
        Assert.Equal(DavXml.Error, root.Name);
        return root;
    }

    /// <summary>The condition the answer names, or null when it carries no body at all.</summary>
    private static XName? ConditionOrNull(DavTestResponse response) =>
        response.Body.Length == 0 ? null : Assert.Single(ErrorRootOf(response).Elements()).Name;

    /// <summary>A context whose first save adding or removing an <paramref name="on"/> throws what
    /// InnoDB throws after waiting <c>innodb_lock_wait_timeout</c>, wrapped as EF wraps it.</summary>
    private sealed class LockedOutDbContext(string databaseName, int number, Type on)
        : PreferencesDbContext(OptionsOf(databaseName))
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            ChangeTracker.Entries().Any(e => e.State is EntityState.Added && e.Entity.GetType() == on)
                ? throw new DbUpdateException("save failed", MySqlErrors.With(number))
                : base.SaveChangesAsync(cancellationToken);

        private static DbContextOptions<PreferencesDbContext> OptionsOf(string name) =>
            new DbContextOptionsBuilder<PreferencesDbContext>()
                .UseInMemoryDatabase(name, PreferencesTestDbContext.Root)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
    }
}
