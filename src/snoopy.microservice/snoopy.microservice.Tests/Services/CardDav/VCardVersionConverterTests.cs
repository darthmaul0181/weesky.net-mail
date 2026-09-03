using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Services.Contacts;
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

    // Legacy Outlook and Apple exports carry one, and the import path stores a .vcf verbatim, so
    // these sit in real books.
    private const string EmbeddedAgentCard =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nN:Lovelace;Ada;;;\r\n" +
        "AGENT:BEGIN:VCARD\\nVERSION:3.0\\nFN:Sec Retary\\nTEL:+3211\\nEND:VCARD\\n\r\n" +
        "END:VCARD\r\n";

    [Fact]
    public void ConvertingAnEmbeddedAgent_ServesExactlyOneCard()
    {
        var converted = VCardVersionConverter.To(EmbeddedAgentCard, "4.0");

        // 4.0 embeds no card, so the writer dereferences the agent into a second BEGIN:VCARD keyed
        // by a UUID minted on the spot. One address-data carries one card, or clients mis-parse it.
        Assert.Equal(1, Occurrences(converted, "BEGIN:VCARD"));
        Assert.Equal(1, Occurrences(converted, "END:VCARD"));
        Assert.DoesNotContain("urn:uuid:", converted);
    }

    [Fact]
    public void ConvertingAnEmbeddedAgent_IsIdenticalTwiceInARow()
    {
        // The minted UUID is time-based. A body that changes on every read while the getetag stays
        // put makes the client re-sync for ever, and nothing anywhere reports an error.
        Assert.Equal(
            VCardVersionConverter.To(EmbeddedAgentCard, "4.0"),
            VCardVersionConverter.To(EmbeddedAgentCard, "4.0"));
    }

    [Fact]
    public void ConvertingATwoOneEmbeddedAgentToThree_ServesExactlyOneCard()
    {
        // The 3.0 writer spells a 2.1 embedded agent out unescaped, and its END:VCARD then closes
        // the outer card: the same defect on the other path, and structurally corrupt besides.
        const string card = "BEGIN:VCARD\r\nVERSION:2.1\r\nUID:u1\r\nFN:Ada\r\n" +
                            "AGENT:BEGIN:VCARD\\nVERSION:2.1\\nFN:Sec\\nEND:VCARD\\n\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "3.0");

        Assert.Equal(1, Occurrences(converted, "BEGIN:VCARD"));
        Assert.Equal(1, Occurrences(converted, "END:VCARD"));
    }

    [Fact]
    public void ConvertingKeepsARelationThatOnlyReferencesACard()
    {
        // Only the embedded card is dropped. A relation naming another card by id costs nothing to
        // convert and is the client's own data: dropping it too would be gratuitous loss.
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\n" +
                            "AGENT;VALUE=URI:urn:uuid:bbbb\r\nEND:VCARD\r\n";

        Assert.Contains("urn:uuid:bbbb", VCardVersionConverter.To(card, "4.0"));
    }

    [Fact]
    public void AnUnreadableCard_IsServedAsStored()
    {
        // No response of this plan is a 500, and a card we cannot parse is still the resource's
        // body: serving it as stored beats failing the whole multistatus over one row.
        const string card = "this is not a vCard at all";

        Assert.Equal(card, VCardVersionConverter.To(card, "4.0"));
    }

    [Fact]
    public void ServingAGroupCardIn40_TranslatesKindAndMembers()
    {
        var v3 = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nN:;;;;\r\n"
            + "X-ADDRESSBOOKSERVER-KIND:group\r\nX-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m1\r\nEND:VCARD\r\n";
        var served = VCardVersionConverter.To(v3, "4.0");
        Assert.Contains("KIND:group", served);
        Assert.Contains("MEMBER:urn:uuid:m1", served);
        Assert.DoesNotContain("X-ADDRESSBOOKSERVER", served);
    }

    [Fact]
    public void ServingAGroupCardIn30_RebuildsFromTheStoredCard()
    {
        // Le writer 3.0 a DÉJÀ supprimé KIND et MEMBER (propriétés 4.0-only) : rien à renommer,
        // les deux lignes se rebâtissent depuis la carte stockée — le test rougit si on l'a écrit
        // comme un renommage (décision 5).
        var v4 = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:g\r\nFN:G\r\nKIND:group\r\n"
            + "MEMBER:urn:uuid:m1\r\nMEMBER:m2\r\nEND:VCARD\r\n";
        var served = VCardVersionConverter.To(v4, "3.0");
        Assert.Contains("X-ADDRESSBOOKSERVER-KIND:group", served);
        Assert.Contains("X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m1", served);
        Assert.Contains("X-ADDRESSBOOKSERVER-MEMBER:m2", served); // valeur verbatim, jamais réécrite
    }

    [Fact]
    public void ServingAGroupCardIn40_RenamesAGroupPrefixedMemberLineWithParameters()
    {
        var v3 = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nN:;;;;\r\n"
            + "X-ADDRESSBOOKSERVER-KIND:group\r\n"
            + "item1.X-ADDRESSBOOKSERVER-MEMBER;X-FOO=bar:urn:uuid:m1\r\nEND:VCARD\r\n";
        var served = VCardVersionConverter.To(v3, "4.0");
        Assert.Contains("item1.MEMBER;X-FOO=bar:urn:uuid:m1", served);
    }

    // Des clients écrivent les deux dialectes sur la même carte. La conversion ne doit pas les
    // additionner : les doublons reviendraient en vcard_raw au PUT suivant.
    [Fact]
    public void ServingAMixedDialectGroupCardIn40_WritesEachLineOnce()
    {
        // m2 n'a pas de jumeau X- : la déduplication porte sur le couple (nom, valeur), donc elle
        // ne doit pas emporter les membres que le second dialecte ne redit pas.
        var mixed = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nN:;;;;\r\n"
            + "KIND:group\r\nMEMBER:urn:uuid:m1\r\nMEMBER:urn:uuid:m2\r\n"
            + "X-ADDRESSBOOKSERVER-KIND:group\r\nX-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m1\r\nEND:VCARD\r\n";

        var served = VCardVersionConverter.To(mixed, "4.0");

        Assert.DoesNotContain("X-ADDRESSBOOKSERVER", served);
        Assert.Equal(1, Lines(served, "KIND"));
        Assert.Equal(2, Lines(served, "MEMBER"));
        Assert.Equal(1, Occurrences(served, "urn:uuid:m1"));
        Assert.Equal(1, Occurrences(served, "urn:uuid:m2"));
    }

    // Des deux lignes qui disent le même membre, celle qui reste est la ligne stockée telle quelle :
    // la bibliothèque resérialise la sienne et perdrait le préfixe de groupe et le paramètre X-.
    [Fact]
    public void ServingAMixedDialectGroupCardIn40_KeepsTheStoredLineVerbatim()
    {
        var mixed = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nN:;;;;\r\n"
            + "KIND:group\r\nMEMBER:urn:uuid:m1\r\n"
            + "X-ADDRESSBOOKSERVER-KIND:group\r\n"
            + "item1.X-ADDRESSBOOKSERVER-MEMBER;X-FOO=bar:urn:uuid:m1\r\nEND:VCARD\r\n";

        var served = VCardVersionConverter.To(mixed, "4.0");

        Assert.Contains("item1.MEMBER;X-FOO=bar:urn:uuid:m1", served);
        Assert.Equal(1, Occurrences(served, "urn:uuid:m1"));
    }

    [Fact]
    public void ServingAMixedDialectGroupCardIn30_WritesEachLineOnce()
    {
        var mixed = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:g\r\nFN:G\r\n"
            + "KIND:group\r\nMEMBER:urn:uuid:m1\r\n"
            + "X-ADDRESSBOOKSERVER-KIND:group\r\nX-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m1\r\nEND:VCARD\r\n";

        var served = VCardVersionConverter.To(mixed, "3.0");

        Assert.Equal(0, Lines(served, "KIND"));
        Assert.Equal(0, Lines(served, "MEMBER"));
        Assert.Equal(1, Lines(served, "X-ADDRESSBOOKSERVER-KIND"));
        Assert.Equal(1, Lines(served, "X-ADDRESSBOOKSERVER-MEMBER"));
        Assert.Equal(1, Occurrences(served, "urn:uuid:m1"));
    }

    // Combien de lignes portent ce nom de propriété — le paramètre que le writer 4.0 ajoute de
    // lui-même (VALUE=URI sur MEMBER) est sa règle, pas la nôtre, et ne doit pas figer le test.
    private static int Lines(string card, string name) =>
        card.Split("\r\n").Count(l =>
            l.StartsWith(name + ":", StringComparison.Ordinal)
            || l.StartsWith(name + ";", StringComparison.Ordinal));

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    // Le REPORT d'un client 3.0 sur une carte stockee en 4.0 passe par la : une photo ecrite par
    // 4f doit en ressortir avec ses octets.
    [Fact]
    public void ConvertingFourToThree_KeepsAnEmbeddedPhoto()
    {
        var value = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0x2A });
        var card = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:u1\r\nFN:A\r\n"
            + $"PHOTO:data:image/jpeg;base64,{value}\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "3.0");

        Assert.Contains(value, VCardComposer.Unfold(converted));
    }
}
