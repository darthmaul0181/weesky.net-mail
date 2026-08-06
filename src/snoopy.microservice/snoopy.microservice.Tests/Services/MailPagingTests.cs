using MailKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailPagingTests
{
    [Theory]
    // total, page, pageSize  =>  startIndex, endIndex
    [InlineData(100, 0, 50, 50, 99)]   // newest 50
    [InlineData(100, 1, 50, 0, 49)]    // next 50
    [InlineData(100, 2, 50, -1, -1)]   // past the end
    [InlineData(30, 0, 50, 0, 29)]     // fewer messages than a page
    [InlineData(0, 0, 50, -1, -1)]     // empty folder
    [InlineData(75, 1, 50, 0, 24)]     // partial last page
    [InlineData(1, 0, 50, 0, 0)]       // single message
    public void ComputePageWindow_MapsNewestFirstPagesToSequenceRanges(
        int total, int page, int pageSize, int expectedStart, int expectedEnd)
    {
        var (start, end) = MailPaging.ComputePageWindow(total, page, pageSize);

        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }

    // ── Sorted paging (IMAP SORT) ───────────────────────────────────────

    [Theory]
    [InlineData(0, 3, new[] { 9, 8, 7 })]
    [InlineData(1, 3, new[] { 6, 5, 4 })]
    [InlineData(3, 3, new[] { 0 })]          // partial last page
    [InlineData(4, 3, new int[0])]           // past the end
    [InlineData(-1, 3, new int[0])]
    [InlineData(0, 0, new int[0])]
    // page * pageSize overflowing int must read as "past the end", never wrap into page 0.
    [InlineData(int.MaxValue, 200, new int[0])]
    public void PageOf_SlicesTheOrderedList(int page, int pageSize, int[] expected)
    {
        int[] newestFirst = [9, 8, 7, 6, 5, 4, 3, 2, 1, 0];

        Assert.Equal(expected, MailPaging.PageOf(newestFirst, page, pageSize));
    }

    // The server may answer a UID FETCH in any order — in practice ascending UID, which is
    // precisely not the sort order asked for. Restoring it is what makes SORT worth using.
    [Fact]
    public void InOrderOf_RestoresTheRequestedOrder()
    {
        var fetched = new[] { (Uid: 4, Subject: "d"), (Uid: 9, Subject: "a"), (Uid: 7, Subject: "b") };

        var ordered = MailPaging.InOrderOf(fetched, [9, 7, 4], item => item.Uid);

        Assert.Equal(["a", "b", "d"], ordered.Select(item => item.Subject));
    }

    [Fact]
    public void InOrderOf_DropsAnItemTheOrderDoesNotMention()
    {
        var fetched = new[] { (Uid: 4, Subject: "d"), (Uid: 99, Subject: "stray") };

        var ordered = MailPaging.InOrderOf(fetched, [4], item => item.Uid);

        Assert.Equal(["d"], ordered.Select(item => item.Subject));
    }

    [Fact]
    public void InOrderOf_SkipsAnOrderEntryThatWasNotFetched()
    {
        var fetched = new[] { (Uid: 4, Subject: "d") };

        var ordered = MailPaging.InOrderOf(fetched, [9, 4], item => item.Uid);

        Assert.Equal(["d"], ordered.Select(item => item.Subject));
    }

    [Theory]
    [InlineData(100, -1, 50)]
    [InlineData(100, 0, 0)]
    [InlineData(100, 0, -10)]
    public void ComputePageWindow_RejectsNonsensicalInput(int total, int page, int pageSize)
    {
        Assert.Equal((-1, -1), MailPaging.ComputePageWindow(total, page, pageSize));
    }

    // ── Merge-path ordering / attachment filter (all-folders, no-SORT) ──

    private static MailPaging.SearchHit Hit(uint uid, DateTimeOffset sortKey, bool hasAttachment = false)
        => new(new UniqueId(uid), null!, sortKey, hasAttachment);

    [Fact]
    public void OrderHits_SortsNewestSentDateFirst()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, d.AddDays(1)), Hit(2, d.AddDays(3)), Hit(3, d.AddDays(2)) };

        var ordered = MailPaging.OrderHits(hits, attachmentsOnly: false);

        Assert.Equal([2u, 3u, 1u], ordered.Select(h => h.Uid.Id));
    }

    [Fact]
    public void OrderHits_SortsByTheKeyRegardlessOfInputOrder()
    {
        var older = new DateTimeOffset(2020, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var recent = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, older), Hit(2, recent) };

        var ordered = MailPaging.OrderHits(hits, attachmentsOnly: false);

        Assert.Equal([2u, 1u], ordered.Select(h => h.Uid.Id));
    }

    [Fact]
    public void SortKeyOf_UsesTheSentDateWhenPresent()
    {
        var sent = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var arrival = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(sent, MailPaging.SortKeyOf(sent, arrival));
    }

    // The robustness path #3 exists for: a malformed message with no Envelope.Date must fall
    // back to its arrival date rather than sort as MinValue or crash.
    [Fact]
    public void SortKeyOf_FallsBackToInternalDateWhenSentDateIsNull()
    {
        var arrival = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(arrival, MailPaging.SortKeyOf(null, arrival));
    }

    [Fact]
    public void SortKeyOf_ReturnsMinValueWhenBothAreNull()
    {
        Assert.Equal(DateTimeOffset.MinValue, MailPaging.SortKeyOf(null, null));
    }

    [Fact]
    public void OrderHits_WhenAttachmentsOnlyDropsHitsWithout()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, d.AddDays(1), hasAttachment: true), Hit(2, d.AddDays(2), hasAttachment: false) };

        var ordered = MailPaging.OrderHits(hits, attachmentsOnly: true);

        Assert.Equal([1u], ordered.Select(h => h.Uid.Id));
    }

    [Fact]
    public void OrderHits_WhenNotAttachmentsOnlyKeepsEveryHit()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, d.AddDays(1), hasAttachment: true), Hit(2, d.AddDays(2), hasAttachment: false) };

        var ordered = MailPaging.OrderHits(hits, attachmentsOnly: false);

        Assert.Equal([2u, 1u], ordered.Select(h => h.Uid.Id));
    }

    [Fact]
    public void FairShares_LetsSmallFoldersKeepTheirsAndSplitsTheRest()
    {
        Assert.Equal(new long[] { 1500, 500 }, MailPaging.FairShares([3000, 500], 2000));
        Assert.Equal(new long[] { 1000, 1000 }, MailPaging.FairShares([1500, 1500], 2000));
        Assert.Equal(new long[] { 100, 200 }, MailPaging.FairShares([100, 200], 2000));
    }
}
