using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// The rule of the whole tranche, tested where a client actually stands: <b>no refusal of the
/// store reaches a DAV client as a 500</b>. Each case asserts the answer it really receives — the
/// status AND the named condition — never the mere absence of a 500, which passes against a wrong
/// code, a missing precondition element and an empty body alike.
/// </summary>
public sealed class CardDavNoFiveHundredTests : IAsyncLifetime
{
    private DavTestServer server = null!;

    private Mock<IDavContactWriter> Writer { get; } = new();

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(overrides: services =>
            services.AddScoped<IDavContactWriter>(_ => Writer.Object));
        DelegateToTheRealWriter();
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Theory]
    [InlineData(DavWriteStatus.InvalidCard, 403, "valid-address-data")]
    [InlineData(DavWriteStatus.UnsupportedVersion, 403, "supported-address-data")]
    [InlineData(DavWriteStatus.UidConflict, 403, "no-uid-conflict")]
    [InlineData(DavWriteStatus.TooLarge, 403, "max-resource-size")]
    [InlineData(DavWriteStatus.BookFull, 507, null)]
    [InlineData(DavWriteStatus.Busy, 503, null)]
    [InlineData(DavWriteStatus.AlreadyExists, 412, null)]
    [InlineData(DavWriteStatus.NotFound, 404, null)]
    public async Task EveryStoreRefusalOnPut_ReachesTheClientAsItsOwnNamedAnswer(
        DavWriteStatus status, int expected, string? condition)
    {
        GivenTheWriterAnswers(status);

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        // Written as one theory over every refusal rather than one test per case: the rule is the
        // tranche's, not one route's, and a 500 here is a client that retries the same card for
        // ever. What is asserted is the answer, though — never "not 500".
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(condition is null ? null : DavXml.CardDav + condition, ConditionOrNull(response));
    }

    [Theory]
    [InlineData(DavWriteStatus.Deleted, 204)]
    [InlineData(DavWriteStatus.NotFound, 404)]
    [InlineData(DavWriteStatus.Busy, 503)]
    public async Task EveryOutcomeOfDelete_ReachesTheClientAsItsOwnCode(
        DavWriteStatus status, int expected)
    {
        await GivenACard("a.vcf");
        GivenTheWriterDeletesWith(status);

        var response = await server.SendAsync("DELETE", DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ADeleteEndingOnACardRefusal_IsStillNotA500()
    {
        // DELETE reads no card so no card refusal can come out of it — but the branch that used to
        // meet one threw, and a throw is the 500 this tranche promises never to answer.
        await GivenACard("a.vcf");
        GivenTheWriterDeletesWith(DavWriteStatus.InvalidCard);

        var response = await server.SendAsync("DELETE", DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "valid-address-data", ConditionOrNull(response));
    }

    [Theory]
    [MemberData(nameof(EveryRealRefusalOfTheStore))]
    public async Task NoRealStoreRefusal_EverBecomesA500(
        string body, int expected, string? condition)
    {
        // The same rule with the REAL writer over the real projection: these are the
        // Result.Failures and framework exceptions written for the webmail's UI, reaching a DAV
        // client through nothing but the translation this task installs.
        var response = await Put(DavPaths.Card(UserId, "a.vcf"), body);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(condition is null ? null : DavXml.CardDav + condition, ConditionOrNull(response));
    }

    public static TheoryData<string, int, string?> EveryRealRefusalOfTheStore() => new()
    {
        // Two cards in one body — an address object resource is ONE vCard (RFC 6352 § 5.1).
        { ValidCard("u1") + ValidCard("u2"), 403, "valid-address-data" },
        { "not a card at all", 403, "valid-address-data" },
        { CardOfVersion("2.1"), 403, "supported-address-data" },
        // Exactly the card ceiling, and one byte past it: the request limit sits above both, so the
        // announced 403 answers — never the transport 413 the announcement never named.
        { CardWithNoUidOfExactly(1024 * 1024), 403, "max-resource-size" },
        { CardWithNoUidOfExactly(1024 * 1024 + 1), 403, "max-resource-size" },
    };

    [Fact]
    public async Task ARealUidConflict_Answers403WithTheHolderHref()
    {
        await GivenACard("a.vcf", "shared");

        var response = await Put(DavPaths.Card(UserId, "b.vcf"), ValidCard("shared"));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "no-uid-conflict", ConditionOrNull(response));
        // Without the href the client knows it lost the UID but not to whom.
        Assert.Equal(DavPaths.Card(UserId, "a.vcf"),
            ErrorRootOf(response).Descendants(DavXml.Href).Single().Value);
    }

    [Fact]
    public async Task ARealFullBook_Answers507()
    {
        await GivenTheBookIsFull();

        // RFC 4918 § 11.5 — the cap is a Result.Failure of the store, and it reaches the client as
        // a status of its own rather than as the 500 an untranslated refusal would be.
        Assert.Equal(507, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"))).StatusCode);
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ALockWaitTimeout_Answers503AndNot500(int number)
    {
        // End to end over the REAL writer, with only the engine faked: InMemory arbitrates no lock
        // and can raise no MySqlException, so the save throws what MariaDB would have thrown,
        // wrapped exactly as EF wraps it.
        GivenTheStoreWillTimeOutOnItsLock(number);

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        Assert.Equal(503, response.StatusCode);
        // The ONE case where retrying is the right conduct, which is why it dates its own retry.
        Assert.Equal("1", response.Header("Retry-After"));
    }

    [Theory]
    [InlineData(MySqlErrors.LockWaitTimeout)]
    [InlineData(MySqlErrors.Deadlock)]
    public async Task ALockWaitTimeoutWhileArchivingARefusedBody_StillAnswers412(int number)
    {
        // The archive rides a 412 whose precondition has ALREADY failed. A 500 here would have the
        // client retry a card the server correctly refused — and fail the same precondition again,
        // for ever. The refusal is the answer; the archive is a courtesy that may be lost.
        await GivenACard("a.vcf");
        GivenTheArchiveWillTimeOutOnItsLock(number);

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifMatch: "\"stale\"");

        Assert.Equal(412, response.StatusCode);
    }

    [Fact]
    public async Task AFaultingStore_IsTheOnlyThingLeftThatTraverses()
    {
        // The honest counterpart, and what keeps every assertion above from being satisfied by a
        // blanket catch: a fault is NOT transient, must not be dressed as a 503 that has every
        // client retry a broken server for ever, and still traverses. This host runs no exception
        // handler, so it surfaces here as the throw the real pipeline turns into the 500.
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .ThrowsAsync(new DbUpdateException("the table is gone"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1")));
    }

    private async Task GivenACard(string davName, string uid = "u1")
    {
        var response = await Put(DavPaths.Card(UserId, davName), ValidCard(uid));
        Assert.Equal(201, response.StatusCode);
    }

    private async Task GivenTheBookIsFull()
    {
        await using var context = server.CreateContext();
        for (var i = 0; i < ContactStore.MaxPerUser; i++)
        {
            context.Contacts.Add(new Contact
            {
                Id = Guid.NewGuid(), UserId = UserId, Uid = Guid.NewGuid().ToString(),
                UpdatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private void GivenTheWriterAnswers(DavWriteStatus status) =>
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .ReturnsAsync(new DavWriteOutcome(status, null,
                status is DavWriteStatus.UidConflict ? DavPaths.Card(UserId, "b.vcf") : null, 0));

    private void GivenTheWriterDeletesWith(DavWriteStatus status) =>
        Writer.Setup(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new DavWriteOutcome(status, null, null, 0));

    /// <summary>
    /// The real writer over a context whose save throws what InnoDB throws when it has waited
    /// <c>innodb_lock_wait_timeout</c> — fifty seconds by default — for an import holding the state
    /// lock until its COMMIT.
    /// </summary>
    private void GivenTheStoreWillTimeOutOnItsLock(int number) =>
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns((Guid user, string name, string card, CancellationToken token, bool createOnly,
                    string? ifMatch) =>
                WithRealWriter(new LockedOutDbContext(server.DatabaseName, number, typeof(Contact)),
                    real => real.PutAsync(user, name, card, token, createOnly, ifMatch)));

    /// <summary>
    /// The real writer whose ARCHIVE save times out: the one write the 412 path performs, and the
    /// last place a transient could still turn a correct refusal into a 500.
    /// </summary>
    private void GivenTheArchiveWillTimeOutOnItsLock(int number) =>
        Writer.Setup(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, string name, string card, CancellationToken token) =>
                WithRealWriter(
                    new LockedOutDbContext(server.DatabaseName, number, typeof(ContactRevision)),
                    real => real.ArchiveRejectedAsync(user, name, card, token)));

    private void DelegateToTheRealWriter()
    {
        Writer.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns((Guid user, string name, string card, CancellationToken token, bool createOnly,
                    string? ifMatch) =>
                WithRealWriter(server.CreateContext(),
                    real => real.PutAsync(user, name, card, token, createOnly, ifMatch)));
        Writer.Setup(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns((Guid user, string name, CancellationToken token, string? ifMatch) =>
                WithRealWriter(server.CreateContext(),
                    real => real.DeleteAsync(user, name, token, ifMatch)));
        Writer.Setup(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, string name, string card, CancellationToken token) =>
                WithRealWriter(server.CreateContext(),
                    real => real.ArchiveRejectedAsync(user, name, card, token)));
    }

    private static async Task<T> WithRealWriter<T>(
        PreferencesDbContext context, Func<IDavContactWriter, Task<T>> call)
    {
        await using (context)
        {
            var sync = new InMemorySyncStore(context);
            return await call(new DavContactWriter(context, new ContactStore(context, sync), sync,
                NullLogger<DavContactWriter>.Instance));
        }
    }

    private async Task<DavTestResponse> Put(string path, string body, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.TryAddWithoutValidation("Content-Type", "text/vcard");
        request.Content = content;
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        using var response = await server.Client.SendAsync(request);
        return await DavTestResponse.ReadAsync(response);
    }

    private static string ValidCard(string uid, string fn = "Grace") =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:{fn}\r\nEND:VCARD\r\n";

    private static string CardOfVersion(string version) =>
        $"BEGIN:VCARD\r\nVERSION:{version}\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

    /// <summary>All ASCII, so characters are bytes; no UID, so the stamp pushes it past the card
    /// ceiling while the body itself still fits the request limit.</summary>
    private static string CardWithNoUidOfExactly(int bytes)
    {
        const string head = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Big\r\nNOTE:";
        const string tail = "\r\nEND:VCARD\r\n";
        return head + new string('x', bytes - head.Length - tail.Length) + tail;
    }

    private static XElement ErrorRootOf(DavTestResponse response)
    {
        var root = XDocument.Parse(response.Body).Root!;
        Assert.Equal(DavXml.Error, root.Name);
        return root;
    }

    /// <summary>The condition the answer names, or null when it carries no body at all — the two
    /// things "not a 500" cannot tell apart.</summary>
    private static XName? ConditionOrNull(DavTestResponse response) =>
        response.Body.Length == 0 ? null : Assert.Single(ErrorRootOf(response).Elements()).Name;

    /// <summary>
    /// A context whose first contact-adding save throws what InnoDB throws after waiting
    /// <c>innodb_lock_wait_timeout</c>, wrapped as EF wraps whatever the provider threw. InMemory
    /// arbitrates no lock, so this is the only way the translation can be exercised at all.
    /// </summary>
    private sealed class LockedOutDbContext(string databaseName, int number, Type on)
        : PreferencesDbContext(OptionsOf(databaseName))
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            ChangeTracker.Entries().Any(e => e.State == EntityState.Added && e.Entity.GetType() == on)
                ? throw new DbUpdateException("save failed", MySqlErrors.With(number))
                : base.SaveChangesAsync(cancellationToken);

        private static DbContextOptions<PreferencesDbContext> OptionsOf(string name) =>
            new DbContextOptionsBuilder<PreferencesDbContext>()
                .UseInMemoryDatabase(name, PreferencesTestDbContext.Root)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
    }
}
