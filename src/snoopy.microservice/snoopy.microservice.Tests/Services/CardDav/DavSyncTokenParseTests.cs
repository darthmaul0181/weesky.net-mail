using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavSyncTokenParseTests
{
    private static readonly Guid Epoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static SyncState State(ulong seq = 100, ulong pruned = 10) => new(Epoch, seq, pruned);

    private static XElement TokenElement(string value) => new(DavXml.Dav + "sync-token", value);

    [Fact]
    public void AnEmptyTokenElement_MeansTheWholeBook()
    {
        var read = DavSyncToken.Read(new XElement(DavXml.Dav + "sync-token"), State());

        // The canonical shape of an initial sync — the only one the RFC defines — and what DAVx5
        // writes literally at first pairing.
        Assert.Equal(SyncTokenKind.Initial, read.Kind);
    }

    [Fact]
    public void AnAbsentTokenElement_IsTreatedTheSameWay()
    {
        var read = DavSyncToken.Read(null, State());

        // Our tolerance, not the RFC's: strictly speaking the request is invalid, but refusing it
        // would answer 403 on an approximate client's first gesture — a book that refuses to pair.
        Assert.Equal(SyncTokenKind.Initial, read.Kind);
    }

    [Fact]
    public void AWellFormedTokenInRange_IsRead()
    {
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/42"), State());

        Assert.Equal(new SyncTokenRead(SyncTokenKind.Sequence, 42), read);
    }

    [Fact]
    public void ATokenAtTheWatermark_IsRead()
    {
        // Ruling BG overrules the plan's "n <= P": P is the highest rank PRUNED, every tombstone
        // above it survives, and a token at P asks only for ranks above it. Refusing it made the
        // server refuse tokens it emits itself (Seq == P after a pruned final deletion) — a loop.
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/10"), State(pruned: 10));

        Assert.Equal(new SyncTokenRead(SyncTokenKind.Sequence, 10), read);
    }

    [Fact]
    public void ATokenJustBelowTheWatermark_IsRefused()
    {
        // n = P - 1 needs rank P's tombstone, exactly the newest one pruning removed. Pinned at
        // P - 1 rather than further down: the defect ruling BG fixed was an off-by-one, so the
        // boundary is held from both sides.
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/9"), State(pruned: 10));

        Assert.Equal(SyncTokenKind.Invalid, read.Kind);
    }

    [Fact]
    public void ATokenEqualToTheCounter_IsRead()
    {
        // The refusal is "n > seq", strictly ahead — a token naming exactly the current sequence is
        // the client already caught up, not a restore. Without this case the boundary is untested:
        // mutating > to >= reddens nothing in the rest of this suite.
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/100"), State(seq: 100));

        Assert.Equal(new SyncTokenRead(SyncTokenKind.Sequence, 100), read);
    }

    [Fact]
    public void ATokenAheadOfTheCounter_IsRefused()
    {
        // A restore onto an older backup, a recreated book, a client that served against another
        // server. Accepting would answer empty — which the client reads as "nothing changed" on a
        // book that changed everything.
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/500"), State(seq: 100));

        Assert.Equal(SyncTokenKind.Invalid, read.Kind);
    }

    [Fact]
    public void ATokenOfAnotherEpoch_IsRefused()
    {
        // Exactly what the plan-a epoch rotation exists to produce.
        var other = Guid.NewGuid();

        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{other}/42"), State());

        Assert.Equal(SyncTokenKind.Invalid, read.Kind);
    }

    [Theory]
    [InlineData("http://sabre.io/ns/sync/42")]
    [InlineData("urn:snoopy:42")]
    // Same length as Prefix, epoch/seq well-formed right after it: a dropped prefix check would
    // slice straight into a valid epoch/seq pair and accept it. Neither shorter case above can
    // catch that, since one is empty and the other too short past the real prefix length.
    [InlineData("http://weesky.net/ns/sunc/22222222-2222-2222-2222-222222222222/42")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222/abc")]
    [InlineData("http://weesky.net/ns/sync/not-a-guid/42")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222/-1")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222/99999999999999999999999")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222")]
    [InlineData("  ")]
    public void ATokenOfAnotherShape_IsRefused(string token)
    {
        // There is nothing to understand in a token we did not issue. The overflow case is in the
        // list on purpose: ulong.Parse throws, and an exception here would be a 500 on a header a
        // client controls.
        Assert.Equal(SyncTokenKind.Invalid, DavSyncToken.Read(TokenElement(token), State()).Kind);
    }

    [Fact]
    public void ATokenOfZero_IsReadAndNotTreatedAsInitial()
    {
        // Zero is a legitimate sequence for a book that has a state row and no write yet. Folding it
        // into Initial would be indistinguishable in effect today and wrong the day it is not.
        // No zero sentinel is involved any more: with the refusal at n < P, n < 0 simply never holds.
        var read = DavSyncToken.Read(
            TokenElement($"{DavSyncToken.Prefix}{Epoch}/0"), State(seq: 5, pruned: 0));

        Assert.Equal(new SyncTokenRead(SyncTokenKind.Sequence, 0), read);
    }

    [Fact]
    public void ReadingNeverThrows()
    {
        // Stated as a test because it is the property that keeps a client-controlled value out of
        // the 500 column: an unreadable token is a refusal to write, not an exception to catch.
        var exception = Record.Exception(() =>
            DavSyncToken.Read(TokenElement("\u0000\uFFFF"), State()));

        Assert.Null(exception);
    }
}
