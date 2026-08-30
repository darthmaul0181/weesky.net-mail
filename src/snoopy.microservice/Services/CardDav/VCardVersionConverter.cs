using FolkerKinzel.VCards;
using FolkerKinzel.VCards.Enums;
using FolkerKinzel.VCards.Extensions;
using FolkerKinzel.VCards.Models;
using weesky.Snoopy.Microservice.Services.Contacts;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Spells a stored card in the version a client asked for. Not a comfort: DAVx5 asks for 4.0 as
/// soon as the announcement carries it and iOS reads 4.0 badly — sabre withdrew its own 4.0
/// announcement in 2013 for exactly that, and only restored it once it shipped this conversion.
/// The transposition rules are the library's, never a textual rewrite of ours.
/// </summary>
internal static class VCardVersionConverter
{
    // The application's options minus UpdateTimeStamp, which stamps REV off the clock: right for a
    // write, wrong here, where it would invent a revision the resource never had and make two
    // reads of one unchanged card differ.
    private static readonly VcfOpts Options =
        VCardComposer.SerializationOptions.Unset(VcfOpts.UpdateTimeStamp);

    /// <summary>
    /// The card as the requested version would spell it — a REPRESENTATION, never a new state. The
    /// stored card stays verbatim and its ETag stays the SHA-256 of what a GET serves.
    /// </summary>
    internal static string To(string card, string version)
    {
        if (Target(version) is not { } wanted) return card;

        IReadOnlyList<VCard> parsed;
        try { parsed = Vcf.Parse(card); }
        catch { return card; }

        // A card we cannot read, or a blob holding several, is served as stored: no response of
        // this plan is a 500, and re-emitting only the first would silently drop the rest.
        if (parsed.Count != 1 || parsed[0].Version == wanted) return card;

        DropEmbeddedCards(parsed[0]);
        var lines = VCardComposer.LogicalLines(Vcf.AsString([parsed[0]], wanted, null, Options));
        VCardComposer.StripNamePlaceholders(lines, parsed[0]);
        RestoreUid(lines, card);
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static VCdVersion? Target(string version) => version switch
    {
        "3.0" => VCdVersion.V3_0,
        "4.0" => VCdVersion.V4_0,
        _ => null,
    };

    /// <summary>
    /// A relation holding a whole card — a 2.1 or 3.0 <c>AGENT</c> — has no correct rendering on
    /// any path this converts. 4.0 cannot embed a card, so the writer dereferences it into a
    /// second <c>BEGIN:VCARD</c> keyed by a UUID minted on the spot: one address-data carrying two
    /// cards, and a body that differs on every read while the getetag does not, which re-syncs the
    /// client for ever without anything reporting an error. The 3.0 writer spells a 2.1 one out
    /// unescaped instead, and the nested <c>END:VCARD</c> then closes the outer card. So the
    /// representation drops what the requested version cannot spell, exactly as the library drops
    /// KIND going to 3.0; the stored card keeps its AGENT verbatim for whoever asks for the version
    /// it already is. A relation that merely references a card by id converts exactly, and stays.
    /// </summary>
    private static void DropEmbeddedCards(VCard card)
    {
        if (card.Relations is not { } relations) return;
        card.Relations = relations.Where(r => r is null || !HoldsAWholeCard(r.Value)).ToList();
    }

    // A 3.0 AGENT parses into a nested VCard; a 2.1 one lands in the reference instead, as a URI
    // whose text is a whole card. Both are the same thing wearing two of the parser's shapes.
    private static bool HoldsAWholeCard(Relation relation)
    {
        if (relation.VCard is not null) return true;
        var reference = relation.ContactID?.Uri?.OriginalString ?? relation.ContactID?.String;
        return reference?.Contains("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// The 4.0 writer labels <c>VALUE=TEXT</c> any UID it cannot read back as a URI, the
    /// <c>urn:uuid:</c> form that is one included. The UID is the identity a client syncs on, so
    /// the stored line goes back verbatim: a card served with another one is another card, which
    /// the client duplicates on its next sync.
    /// </summary>
    private static void RestoreUid(List<string> lines, string card)
    {
        if (VCardComposer.RawUid(card) is not { } raw) return;
        var index = lines.FindIndex(line => VCardComposer
            .NameOf(VCardComposer.Unfold(line)).Equals("UID", StringComparison.OrdinalIgnoreCase));
        if (index >= 0) lines[index] = VCardComposer.Fold(raw);
    }
}
