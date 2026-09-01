using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class DavContactReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static PreferencesTestDbContext NewContextWith(params Contact[] contacts)
    {
        var context = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        context.Contacts.AddRange(contacts);
        context.SaveChanges();

        return context;
    }

    private static Contact Visible(string davName) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Uid = Guid.NewGuid().ToString(),
        DavName = davName,
        VCardRaw = "BEGIN:VCARD\r\nVERSION:3.0\r\nEND:VCARD\r\n",
        CardHash = $"hash-{davName}",
        UpdatedAt = DateTime.UtcNow,
        SyncSequence = 1,
    };

    private static Contact AtRank(string davName, ulong rank)
    {
        var contact = Visible(davName);
        contact.SyncSequence = rank;
        return contact;
    }

    private static Contact Group(string davName)
    {
        var contact = Visible(davName);
        contact.Kind = ContactKinds.Group;
        return contact;
    }

    private static Contact WithoutName()
    {
        var contact = Visible(Guid.NewGuid().ToString());
        contact.DavName = null;
        return contact;
    }

    private static Contact WithoutCard(string davName)
    {
        var contact = Visible(davName);
        contact.VCardRaw = null;
        return contact;
    }

    private static Contact WithEmptyHash(string davName)
    {
        var contact = Visible(davName);
        contact.CardHash = "";
        return contact;
    }

    [Fact]
    public async Task ItStreamsTheVisibleCards()
    {
        using var context = NewContextWith(
            Visible("a.vcf"), Visible("b.vcf"));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, ulong.MaxValue, CancellationToken.None).ToListAsync();

        Assert.Equal(["a.vcf", "b.vcf"], cards.Select(c => c.DavName).Order());
    }

    [Fact]
    public async Task ItStreamsAGroupCardLikeAnyOther()
    {
        using var context = NewContextWith(Visible("a.vcf"), Group("g.vcf"));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, ulong.MaxValue, CancellationToken.None).ToListAsync();

        // The DAV side filters on neither species: the collection serves both, and a client whose
        // groups vanished from it would delete them locally on the next sync (décision 4).
        Assert.Equal(["a.vcf", "g.vcf"], cards.Select(c => c.DavName).Order());
    }

    [Fact]
    public async Task Streaming_StopsAtTheUpperBound_Inclusively()
    {
        using var context = NewContextWith(AtRank("at.vcf", 20), AtRank("past.vcf", 21));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, 20, CancellationToken.None).ToListAsync();

        // Inclusive: the caller's bound IS the rank of the most recent write it read, so excluding
        // it would drop the very card that write created.
        Assert.Equal(["at.vcf"], cards.Select(c => c.DavName));
    }

    [Fact]
    public async Task ACardWithNoName_IsInvisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithoutName());
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, ulong.MaxValue, CancellationToken.None).ToListAsync();

        // The backfill has not reached it. Serving it would build an href from a name that does not
        // exist, and a book that serves a dead href is one a client flags in error every cycle.
        Assert.Single(cards);
    }

    [Fact]
    public async Task ACardWithNoBody_IsInvisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithoutCard("b.vcf"));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, ulong.MaxValue, CancellationToken.None).ToListAsync();

        // The second of the three conditions: the 4a backfill missed this one, so it would go out
        // with an empty body.
        Assert.Single(cards);
    }

    [Fact]
    public async Task ACardWithAnEmptyHash_IsInvisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithEmptyHash("b.vcf"));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, ulong.MaxValue, CancellationToken.None).ToListAsync();

        // The third, and the one no assertion normally looks at: an ETag of "" is syntactically
        // valid and semantically false, and a client files it like any other value, for ever.
        Assert.Single(cards);
    }

    [Fact]
    public async Task AnotherUsersCard_IsNotFound()
    {
        using var context = NewContextWith(Visible("a.vcf"));
        var reader = new DavContactReader(context);

        Assert.Null(await reader.FindAsync(Guid.NewGuid(), "a.vcf", CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_AnswersEveryFieldOfTheProjection()
    {
        // Every one of the seven values is distinct from every other: a swap anywhere in the
        // projection (Uid <-> CardHash caught the mutation this pins) must fail this test.
        var contact = Visible("full.vcf");
        contact.Uid = "the-uid";
        contact.CardHash = "the-hash";
        contact.UpdatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        contact.SyncSequence = 42;
        using var context = NewContextWith(contact);
        var reader = new DavContactReader(context);

        var card = await reader.FindAsync(UserId, "full.vcf", CancellationToken.None);

        Assert.NotNull(card);
        Assert.Equal(contact.Id, card.ContactId);
        Assert.Equal("full.vcf", card.DavName);
        Assert.Equal("the-uid", card.Uid);
        Assert.Equal(contact.VCardRaw, card.VCardRaw);
        Assert.Equal("the-hash", card.CardHash);
        Assert.Equal(contact.UpdatedAt, card.UpdatedAt);
        Assert.Equal(42ul, card.SyncSequence);
    }

    [Fact]
    public async Task FindAsync_AnswersNullForACardThatExistsAndIsOwnedButIsInvisible()
    {
        // The mutation this pins bypassed Visible in FindAsync directly: owner and name still
        // matched, only the empty-hash condition was dropped.
        using var context = NewContextWith(WithEmptyHash("a.vcf"));
        var reader = new DavContactReader(context);

        Assert.Null(await reader.FindAsync(UserId, "a.vcf", CancellationToken.None));
    }

    [Fact]
    public async Task FindingMany_SkipsACardThatIsInvisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithEmptyHash("b.vcf"));
        var reader = new DavContactReader(context);

        var found = await reader.FindManyAsync(UserId, ["a.vcf", "b.vcf"], CancellationToken.None);

        Assert.Equal(["a.vcf"], found.Select(c => c.DavName));
    }

    [Fact]
    public async Task FindingByName_IsCaseSensitive()
    {
        using var context = NewContextWith(Visible("Carte.vcf"));
        var reader = new DavContactReader(context);

        // The column collates utf8mb4_bin: two names differing only by case are two different URLs
        // for every HTTP client, and a case-insensitive collation would make them a uniqueness
        // conflict where the protocol sees two resources.
        Assert.NotNull(await reader.FindAsync(UserId, "Carte.vcf", CancellationToken.None));
        Assert.Null(await reader.FindAsync(UserId, "carte.vcf", CancellationToken.None));
    }

    [Fact]
    public async Task FindingMany_SkipsWhatTheUserDoesNotOwn()
    {
        using var context = NewContextWith(Visible("a.vcf"), Visible("b.vcf"));
        var reader = new DavContactReader(context);

        var found = await reader.FindManyAsync(
            UserId, ["a.vcf", "missing.vcf", "b.vcf"], CancellationToken.None);

        // A stale name in a client's list is a common case, not a fault: the caller answers 404
        // inside the multistatus for each one that did not come back.
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task FindingMany_AnswersOnceForANameAskedTwice()
    {
        // The shape is `Where(c => names.Contains(c.DavName))`, one query. A loop over the names —
        // five thousand round trips on a report whose whole point is to be a batch read — would
        // answer three cards here instead of two, which is what this pins.
        using var context = NewContextWith(Visible("a.vcf"), Visible("b.vcf"));
        var reader = new DavContactReader(context);

        var found = await reader.FindManyAsync(
            UserId, ["a.vcf", "a.vcf", "b.vcf"], CancellationToken.None);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Counting_CountsOnlyWhatIsVisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithoutName(), WithEmptyHash("c.vcf"));
        var reader = new DavContactReader(context);

        Assert.Equal(1, await reader.CountAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Streaming_StopsWhenTheCallerCancels()
    {
        using var context = NewContextWith(Visible("a.vcf"), Visible("b.vcf"));
        var reader = new DavContactReader(context);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Without the token reaching MoveNextAsync, a client that walks away from a gigabyte book
        // leaves the read running to its end. The attribute alone does not carry it.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in reader.StreamAsync(UserId, ulong.MaxValue, cancelled.Token)) { }
        });
    }
}
