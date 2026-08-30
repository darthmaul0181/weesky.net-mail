using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// A card turned into an import row, through the projector rather than a second reading of the
/// card: a <c>.vcf</c> then enters the merge of slice 3d as it is, with the <c>UID</c> as the key
/// placed before the address and the name. The card itself travels verbatim (décision 1).
/// </summary>
internal static class VCardImportMapper
{
    internal static ContactImportRow Map(VCardChunk chunk)
    {
        var projection = VCardProjector.Project(chunk.Text);
        // A card naming nobody but itself still has to reach the merge: the display name is where
        // the CSV mapper looks next too, and no vCard property carries the favourite.
        var nickname = projection.Nickname
            ?? (projection.FirstName == null && projection.LastName == null ? projection.DisplayName : null);

        return new ContactImportRow(
            chunk.Line, projection.FirstName, projection.LastName, nickname, false,
            [.. projection.Addresses.Select(e => e.Address)], chunk.Text, UidOf(chunk.Text),
            Offered(projection, nickname));
    }

    /// <summary>
    /// What the card offers a merge, in the shape the CSV path already hands the store: the store
    /// keeps of it only what the target does not hold. Positions are dropped — the ranks of the
    /// incoming card mean nothing on the target's own, where a fill only ever appends.
    /// </summary>
    private static ContactWrite Offered(ContactProjection card, string? nickname) =>
        new(card.FirstName, card.LastName, nickname, card.DisplayName, card.MiddleName,
            card.NamePrefix, card.NameSuffix, card.Organization, card.Department, card.JobTitle,
            card.Birthday, card.Website, card.Notes, false,
            [.. card.Addresses.Select(e => new ContactWriteEmail(null, e.Address, e.Line.Type))],
            [.. card.Phones.Select(p => new ContactWritePhone(null, p.Number, p.Line.Type))],
            [.. card.PostalAddresses.Select(a => new ContactWriteAddress(null, a.Line.Type, a.PoBox,
                a.Extended, a.Street, a.Locality, a.Region, a.PostalCode, a.Country))],
            "imported");

    /// <summary>
    /// The UID as the card writes it, untruncated: the projection mirrors the column's 255
    /// characters, and a UID cut to fit is a synchronisation identity the card does not carry —
    /// the import refuses the line instead (décision 14).
    /// </summary>
    internal static string? UidOf(string card) => RawValueOf(card, "UID");

    /// <summary>The first raw value of one property, as the card writes it — never decoded.</summary>
    internal static string? RawValueOf(string card, string property)
    {
        foreach (var line in Unfolded(card))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var name = line[..colon].Split(';')[0];
            var dot = name.LastIndexOf('.');
            if (!name[(dot + 1)..].Trim().Equals(property, StringComparison.OrdinalIgnoreCase)) continue;
            return line[(colon + 1)..] is { Length: > 0 } value ? value : null;
        }

        return null;
    }

    private static IEnumerable<string> Unfolded(string card) =>
        card.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n ", string.Empty)
            .Replace("\n\t", string.Empty).Split('\n');
}
