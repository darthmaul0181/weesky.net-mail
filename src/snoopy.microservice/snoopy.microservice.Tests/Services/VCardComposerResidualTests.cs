using System.Text;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The four defects <c>docs/superpowers/contacts-4a-residuals.md</c> deferred while they were
/// unreachable or cosmetic. 4c-ii-a makes the webmail one of two writers and serves the composed
/// card to third-party clients, which turns two of them into routine data loss.
/// </summary>
public sealed class VCardComposerResidualTests
{
    private const string Uid = "u1";

    private static string Card(string version, params string[] lines) =>
        $"BEGIN:VCARD\r\nVERSION:{version}\r\nUID:{Uid}\r\nFN:X\r\n"
        + string.Concat(lines.Select(l => l + "\r\n")) + "END:VCARD\r\n";

    private static ContactWrite Write(
        string? website = null, IReadOnlyList<ContactWriteEmail>? addresses = null) =>
        new(null, null, null, "X", null, null, null, null, null, null, null, website, null, false,
            addresses ?? [], [], [], "manual");

    private static string Unfold(string card) =>
        card.Replace("\r\n ", string.Empty).Replace("\r\n\t", string.Empty);

    [Fact]
    public void AFamilyFallingBackToOneOccurrence_KeepsItsParameters()
    {
        // "The one that counts": 4b made this reachable from the editor, 4c makes it reachable from
        // any phone, and what disappears is found nowhere else. The 3.0 writer re-renders a lone
        // occurrence from the model, and its URL builder emits no parameter at all.
        var stored = Card("3.0", "item4.URL;X-ABLabel=Perso;type=pref:https://exemple.example/a");

        var composed = VCardComposer.Compose(stored, Uid, Write(website: "https://exemple.example/b"));

        // Parameters from the stored line, value from the model.
        Assert.Contains(
            "item4.URL;X-ABLabel=Perso;type=pref:https://exemple.example/b", Unfold(composed));
    }

    [Fact]
    public void Folding_NeverSplitsASurrogatePair()
    {
        // Until this slice a folded card only went to the database; it is now served to third-party
        // clients, and a card cut in the middle of a character is invalid UTF-8 on the wire. The
        // reachable path is Fold: the library folds its own lines, the composer folds the ones it
        // repairs — here an X- parameter the 3.0 EMAIL writer drops and RestoreParams puts back.
        var label = new string('L', 39) + "\U0001F600" + new string('M', 20);
        var stored = Card("3.0", $"EMAIL;TYPE=HOME;X-ABLabel={label}:a@b.example");

        var composed = VCardComposer.Compose(stored, Uid,
            Write(addresses: [new ContactWriteEmail(0, "a@b.example", "HOME")]));

        // The pair survives: the card unfolds to the whole character, and it encodes at all.
        Assert.Contains("\U0001F600", Unfold(composed));
        Assert.Null(Record.Exception(() => new UTF8Encoding(false, true).GetBytes(composed)));
    }

    [Fact]
    public void AUidThatLooksLikeAUri_IsNotRelabelledValueText()
    {
        // The UID's value does not turn on the production path — the column comes from
        // VCardImportMapper.UidOf, a textual scan that keeps the prefix. Only a VALUE=TEXT label is
        // added on a URI-shaped value: cosmetically non-conforming, and now served to real clients.
        const string uid = "urn:uuid:6f4b2c1a-8d3e-4f5a-b1c2-d3e4f5a6b7c8";
        var stored = $"BEGIN:VCARD\r\nVERSION:4.0\r\nUID:{uid}\r\nFN:X\r\nEND:VCARD\r\n";

        var composed = VCardComposer.Compose(stored, uid, Write());

        Assert.Contains($"UID:{uid}", composed);
        Assert.DoesNotContain("VALUE=TEXT", composed);
    }

    [Fact]
    public void TheProjector_StopsAtTheFirstEndVcard()
    {
        // Unreachable while the splitter guaranteed one card per chunk. The PUT of plan c becomes a
        // second producer of vcard_raw, so the guarantee stops holding at the entrance.
        var two =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:First\r\n" +
            "EMAIL;TYPE=HOME:a@b.example\r\nEND:VCARD\r\n" +
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u2\r\nFN:Second\r\n" +
            "EMAIL;TYPE=WORK:c@d.example\r\nBDAY:2000-01-01\r\nEND:VCARD\r\n";

        var projected = VCardProjector.Project(two);

        Assert.Equal("First", projected.DisplayName);
        // The second card used to feed the raw scan: its BDAY became the first card's, and its
        // EMAIL desynchronised the ranks, costing the first card's own parameter block.
        Assert.Null(projected.Birthday);
        Assert.Equal("TYPE=HOME", projected.Addresses[0].Line.Params);
    }
}
