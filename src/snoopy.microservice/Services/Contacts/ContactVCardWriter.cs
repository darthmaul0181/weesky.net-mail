namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// Turns the columns the tables do not model into a vCard 3.0, stored verbatim in
/// <c>contacts.vcard_raw</c>. Nothing reads it yet: it is what stops a phone number from being
/// destroyed by an import, per the slice 3a rule that an unstored property is found nowhere.
/// </summary>
internal static class ContactVCardWriter
{
    private const string Break = "\r\n";

    private static readonly (string Key, string Property)[] Phones =
    [
        ("mobilephone", "TEL;TYPE=CELL"),
        ("othermobile", "TEL;TYPE=CELL"),
        ("homephone", "TEL;TYPE=HOME,VOICE"),
        ("businessphone", "TEL;TYPE=WORK,VOICE"),
        ("homefax", "TEL;TYPE=HOME,FAX"),
        ("businessfax", "TEL;TYPE=WORK,FAX"),
        ("otherphone", "TEL;TYPE=VOICE"),
    ];

    private static readonly (string Key, string Property)[] Scalars =
    [
        ("jobtitle", "TITLE"),
        ("notes", "NOTE"),
        ("birthday", "BDAY"),
        ("webpage", "URL"),
    ];

    internal static string? Write(ContactCsvRow row)
    {
        var properties = new List<string>();

        foreach (var (key, property) in Phones)
            if (Value(row, key) is { } phone) properties.Add($"{property}:{Escape(phone)}");

        if (Value(row, "company") != null || Value(row, "department") != null)
            properties.Add($"ORG:{Escape(Value(row, "company"))};{Escape(Value(row, "department"))}");

        AppendAddress(properties, row, "home", "HOME", null);
        AppendAddress(properties, row, "business", "WORK", Value(row, "officelocation"));

        foreach (var (key, property) in Scalars)
            if (Value(row, key) is { } scalar) properties.Add($"{property}:{Escape(scalar)}");

        var middle = Value(row, "middlename");
        var honorific = Value(row, "title");
        if (properties.Count == 0 && middle == null && honorific == null) return null;

        var card = new List<string>
        {
            "BEGIN:VCARD",
            "VERSION:3.0",
            $"N:{Escape(row.LastName)};{Escape(row.FirstName)};{Escape(middle)};{Escape(honorific)};",
            $"FN:{Escape(FullName(row, middle))}",
        };
        if (row.Nickname != null) card.Add($"NICKNAME:{Escape(row.Nickname)}");
        card.AddRange(row.Addresses.Select(a => $"EMAIL;TYPE=INTERNET:{Escape(a)}"));
        card.AddRange(properties);
        card.Add("END:VCARD");

        return string.Join(Break, card) + Break;
    }

    // The seven vCard 3.0 components: po-box, extended, street, locality, region, code, country.
    // "Office Location" is the extended slot — the one place it means what it says.
    private static void AppendAddress(
        List<string> properties, ContactCsvRow row, string prefix, string type, string? extended)
    {
        string?[] parts =
        [
            null, extended, Value(row, $"{prefix}street"), Value(row, $"{prefix}city"),
            Value(row, $"{prefix}state"), Value(row, $"{prefix}postalcode"), Value(row, $"{prefix}country"),
        ];
        if (parts.All(p => p == null)) return;

        properties.Add($"ADR;TYPE={type}:{string.Join(';', parts.Select(Escape))}");
    }

    private static string? Value(ContactCsvRow row, string key) =>
        row.Extras.TryGetValue(key, out var value) ? value : null;

    private static string FullName(ContactCsvRow row, string? middle)
    {
        var parts = new[] { row.FirstName, middle, row.LastName }.Where(p => p != null);
        var full = string.Join(' ', parts);
        return full.Length > 0 ? full : row.Nickname ?? row.Addresses.FirstOrDefault() ?? string.Empty;
    }

    // Backslash first, or every escape written after it gets escaped a second time.
    private static string Escape(string? value) =>
        value == null ? string.Empty
            : value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
                   .Replace("\r\n", "\\n").Replace("\n", "\\n");
}
