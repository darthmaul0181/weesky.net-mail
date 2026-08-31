using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class VCardSplitterTests
{
    private const string First =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ana\r\nNOTE:une note tres longue qui\r\n  se replie\r\nEND:VCARD\r\n";

    private const string Second = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Bo\r\nEND:VCARD\r\n";

    // The whole point of splitting before parsing: each card keeps its own bytes, folding included.
    [Fact]
    public void Split_KeepsEachCardVerbatimWithItsLine()
    {
        var chunks = VCardSplitter.Split(First + Second);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(First, chunks[0].Text);
        Assert.Equal(1, chunks[0].Line);
        Assert.Equal(Second, chunks[1].Text);
        Assert.Equal(7, chunks[1].Line);
    }

    [Fact]
    public void Split_IgnoresWhatLiesOutsideACard()
    {
        var chunks = VCardSplitter.Split("garbage\r\n\r\n" + First + "trailing text\r\n");

        Assert.Equal(First, Assert.Single(chunks).Text);
        Assert.Equal(3, chunks[0].Line);
    }

    // Tolerance, not silence: the fragment becomes a chunk and the reader decides what it is.
    [Fact]
    public void Split_KeepsAFragmentWithNoEnd()
    {
        var chunks = VCardSplitter.Split("BEGIN:VCARD\r\nFN:Ana\r\n");

        Assert.Equal("BEGIN:VCARD\r\nFN:Ana\r\n", Assert.Single(chunks).Text);
    }

    // What makes the projector's and the composer's bound to the first END:VCARD safe.
    [Fact]
    public void Split_ClosesAnUnterminatedCardOnTheNextBegin()
    {
        var chunks = VCardSplitter.Split("BEGIN:VCARD\r\nFN:Ana\r\n" + Second);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("BEGIN:VCARD\r\nFN:Ana\r\n", chunks[0].Text);
        Assert.Equal(Second, chunks[1].Text);
        Assert.Equal(3, chunks[1].Line);
    }

    [Fact]
    public void Split_ReadsTheBoundariesWhateverTheirCase()
    {
        var chunks = VCardSplitter.Split("begin:vcard\nFN:Ana\nEnd:VCard\n");

        Assert.Equal("begin:vcard\nFN:Ana\nEnd:VCard\n", Assert.Single(chunks).Text);
    }

    // A continuation line is not a boundary: a folded value may spell one at the margin.
    [Fact]
    public void Split_NeverStartsACardOnAFoldedLine()
    {
        var chunks = VCardSplitter.Split("BEGIN:VCARD\r\nNOTE:x\r\n BEGIN:VCARD\r\nEND:VCARD\r\n");

        Assert.Equal("BEGIN:VCARD\r\nNOTE:x\r\n BEGIN:VCARD\r\nEND:VCARD\r\n", Assert.Single(chunks).Text);
    }

    [Fact]
    public void Split_AnswersNothingForAFileWithNoCard()
    {
        Assert.Empty(VCardSplitter.Split("First Name,E-mail Address\r\nBruno,bruno@example.com\r\n"));
    }
}
