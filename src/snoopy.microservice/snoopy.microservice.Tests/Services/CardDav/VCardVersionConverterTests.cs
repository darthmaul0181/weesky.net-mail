using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class VCardVersionConverterTests
{
    [Fact]
    public void ConvertingToTheVersionItAlreadyIs_ChangesNothing()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

        Assert.Equal(card, VCardVersionConverter.To(card, "3.0"));
    }

    [Fact]
    public void ConvertingThreeToFour_RewritesTheVersionLine()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        Assert.Contains("VERSION:4.0", converted);
        Assert.DoesNotContain("VERSION:3.0", converted);
    }

    [Fact]
    public void ConvertingThreeToFour_TransposesPreference()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nTEL;TYPE=CELL,PREF:+32\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        // What one version cannot carry is transposed by the two formats' public rules.
        Assert.Contains("PREF=1", converted);
        Assert.DoesNotContain(",PREF", converted);
    }

    [Fact]
    public void ConvertingFourToThree_TransposesItBack()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:u1\r\nFN:A\r\nTEL;PREF=1:+32\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "3.0");

        Assert.Contains("PREF", converted);
        Assert.DoesNotContain("PREF=1", converted);
    }

    [Fact]
    public void ConvertingKeepsTheUidVerbatim()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:urn:uuid:aaaa\r\nFN:A\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        // The UID is the identity a client syncs on: a card that goes out with a different one is a
        // different card, which the client duplicates on its next sync. The 4.0 writer labels
        // VALUE=TEXT anything it cannot read back as a URI, urn:uuid: included.
        Assert.Contains("UID:urn:uuid:aaaa", converted);
    }

    [Fact]
    public void ConvertingDoesNotTouchTheStoredCard()
    {
        // Stated as a test because it is the whole point: converting on read touches no 4a
        // invariant. The stored card stays verbatim and its ETag stays the SHA-256 of what a GET
        // serves — a converted card is a REPRESENTATION, not a new state.
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        // Comparing the constant to itself could never fail; what can is the conversion handing
        // back the very string it was given, or a second one differing from the first.
        Assert.NotSame(card, converted);
        Assert.NotEqual(card, converted);
        Assert.Equal(converted, VCardVersionConverter.To(card, "4.0"));
    }

    [Fact]
    public void ConvertingStampsNoRevisionOfItsOwn()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n";

        // A REV the stored card never carried is a revision invented on the way out, and one read
        // from the clock makes two reads of one unchanged card differ.
        Assert.DoesNotContain("REV", VCardVersionConverter.To(card, "4.0"));
    }

    [Fact]
    public void ConvertingKeepsTheStoredRevision()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nREV:2020-01-02T03:04:05Z\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n";

        // 4.0 spells a timestamp in the basic ISO form; the instant is the card's own either way.
        Assert.Contains("REV:20200102T030405Z", VCardVersionConverter.To(card, "4.0"));
    }

    [Fact]
    public void ConvertingToThreeInventsNoName()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:u1\r\nTEL;PREF=1:+32\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "3.0");

        // N is mandatory in 3.0 so one must be written, but the library fills it with a question
        // mark, which every client then displays as the contact's name.
        Assert.DoesNotContain("?", converted);
        Assert.Contains("N:;;;;", converted);
    }

    [Fact]
    public void ConvertingKeepsGroupsAndNonStandardProperties()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\n" +
                            "item1.EMAIL:a@b.c\r\nitem1.X-ABLabel:Work\r\nX-FOO;X-BAR=1:v\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        Assert.Contains("item1.EMAIL:a@b.c", converted);
        Assert.Contains("item1.X-ABLabel:Work", converted);
        Assert.Contains("X-FOO;X-BAR=1:v", converted);
    }

    [Fact]
    public void AVerbatimTwoOneCard_IsConvertedToWhatWasAsked()
    {
        // A .vcf imported verbatim is stored byte for byte, 2.1 included: "as stored" is not always
        // one of the two versions we announce.
        const string card = "BEGIN:VCARD\r\nVERSION:2.1\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n";

        Assert.Contains("VERSION:3.0", VCardVersionConverter.To(card, "3.0"));
    }

    [Fact]
    public void AnUnreadableCard_IsServedAsStored()
    {
        // No response of this plan is a 500, and a card we cannot parse is still the resource's
        // body: serving it as stored beats failing the whole multistatus over one row.
        const string card = "this is not a vCard at all";

        Assert.Equal(card, VCardVersionConverter.To(card, "4.0"));
    }
}
