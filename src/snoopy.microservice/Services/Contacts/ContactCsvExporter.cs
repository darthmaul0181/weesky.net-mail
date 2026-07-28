using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// The book as a file. The first address goes in the column Rainloop and Outlook understand, the
/// rest in columns only we read back — so the export is complete here and usable elsewhere rather
/// than truncated on both sides.
/// </summary>
internal static class ContactCsvExporter
{
    // A field opening on one of these is a formula to Excel and LibreOffice, not a name.
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r', '\''];

    internal static byte[] Write(IReadOnlyList<ContactView> contacts)
    {
        var ordered = contacts
            .OrderBy(SortKey, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(c => c.Id)
            .ToList();
        var addressColumns = ordered.Count == 0 ? 1 : Math.Max(1, ordered.Max(c => c.Addresses.Count));

        List<string> header = ["First Name", "Last Name", "Nick Name", "Display Name", "E-mail Address"];
        for (var i = 2; i <= addressColumns; i++) header.Add($"E-mail {i} Address");
        header.Add("Favorite");

        return CsvWriter.Write(header, ordered.Select(contact => Row(contact, addressColumns)));
    }

    private static IReadOnlyList<string> Row(ContactView contact, int addressColumns)
    {
        List<string> fields =
        [
            Neutralise(contact.FirstName ?? string.Empty),
            Neutralise(contact.LastName ?? string.Empty),
            Neutralise(contact.Nickname ?? string.Empty),
            Neutralise(NameOf(contact)),
        ];
        for (var i = 0; i < addressColumns; i++)
            fields.Add(i < contact.Addresses.Count ? contact.Addresses[i] : string.Empty);
        fields.Add(contact.IsFavorite ? "true" : string.Empty);

        return fields;
    }

    /// <summary>
    /// A leading apostrophe, which a spreadsheet eats on read, so a crafted name lands as text
    /// rather than as a formula. The apostrophe is a trigger itself, so
    /// <see cref="ContactCsvMapper"/>'s strip is symmetric and a name really beginning with one
    /// survives the round trip. Names only: a trigger-led address is forced into an unquoted
    /// local part, which always carries a mandatory <c>@</c> and non-empty domain — never a
    /// parseable formula in Excel or LibreOffice. The design doc carries the full argument.
    /// </summary>
    private static string Neutralise(string field) =>
        field.Length > 0 && FormulaStarts.Contains(field[0]) ? $"'{field}" : field;

    /// <summary>
    /// Mirrors the frontend's displayNameOf, minus its address fallback: written verbatim, an
    /// address would come back as a nickname on the next import — a name nobody typed.
    /// </summary>
    private static string NameOf(ContactView contact)
    {
        var full = string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(n => n != null));
        return full.Length > 0 ? full : contact.Nickname ?? string.Empty;
    }

    // Deterministic order: the list endpoint has none, and a file whose rows move between two
    // exports of an unchanged book is undiffable. The Id tiebreaker in Write matters here — two
    // contacts sharing a SortKey would otherwise keep ListAsync's unspecified order between runs.
    private static string SortKey(ContactView contact)
    {
        var name = NameOf(contact);
        return name.Length > 0 ? name : contact.Addresses.FirstOrDefault() ?? string.Empty;
    }
}
