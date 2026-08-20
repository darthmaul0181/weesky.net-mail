using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// The book as a file, in the Outlook column set 3d already reads on import — adding columns makes
/// the file symmetric, since the mapper is what merges every one of them back in on the way back.
/// The first address goes in the column Rainloop and Outlook understand, the rest in columns only
/// we read back.
/// </summary>
internal static class ContactCsvExporter
{
    // A field opening on one of these is a formula to Excel and LibreOffice, not a name.
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r', '\''];

    // A phone column's security boundary: a value built only from these characters can, at worst,
    // open a formula on '+'/'-' and compute a constant ("+1+1" renders "2") — it has no letter to
    // name a function, no '|'/'!'/quote for a DDE call, so it can never reference another cell or
    // invoke anything. That's why it's safe to leave for a foreign importer to read as a number.
    private static readonly char[] PhoneChars = [' ', '+', '-', '(', ')', '.', '/'];

    // Every phone falls into exactly one bucket; the header order below is not the check order —
    // CELL, then a fax combination, then a bare HOME/WORK, are checked in that priority, but the
    // header lists Mobile before the fax columns.
    private enum PhoneBucket { Mobile, Home, Business, HomeFax, BusinessFax, Other }

    private static readonly (string Header, PhoneBucket Bucket)[] PhoneColumns =
    [
        ("Mobile Phone", PhoneBucket.Mobile), ("Home Phone", PhoneBucket.Home),
        ("Business Phone", PhoneBucket.Business), ("Home Fax", PhoneBucket.HomeFax),
        ("Business Fax", PhoneBucket.BusinessFax), ("Other Phone", PhoneBucket.Other),
    ];

    // Home first, Business second — the order both the header and a row fill their postal block in.
    private static readonly (string Prefix, string TypeToken)[] PostalGroups = [("Home", "HOME"), ("Business", "WORK")];

    private static readonly (string Suffix, Func<ContactDetailAddress, string?> Value)[] PostalFields =
    [
        ("Street", a => a.Street), ("City", a => a.Locality), ("State", a => a.Region),
        ("Postal Code", a => a.PostalCode), ("Country", a => a.Country),
    ];

    internal static byte[] Write(IReadOnlyList<ContactDetail> contacts)
    {
        var ordered = contacts
            .OrderBy(SortKey, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(c => c.Id)
            .ToList();
        var addressColumns = ordered.Count == 0 ? 1 : Math.Max(1, ordered.Max(c => c.Addresses.Count));

        List<string> header =
        [
            "Title", "First Name", "Middle Name", "Last Name", "Nick Name", "Display Name",
            "Company", "Department", "Job Title", "E-mail Address",
        ];
        for (var i = 2; i <= addressColumns; i++) header.Add($"E-mail {i} Address");
        header.AddRange(["Notes", "Web Page", "Birthday"]);
        header.AddRange(PhoneColumns.Select(c => c.Header));
        foreach (var (prefix, _) in PostalGroups) header.AddRange(PostalFields.Select(f => $"{prefix} {f.Suffix}"));
        header.Add("Favorite");

        return CsvWriter.Write(header, ordered.Select(contact => Row(contact, addressColumns)));
    }

    private static IReadOnlyList<string> Row(ContactDetail contact, int addressColumns)
    {
        List<string> fields =
        [
            Neutralise(contact.NamePrefix ?? string.Empty),
            Neutralise(contact.FirstName ?? string.Empty),
            Neutralise(contact.MiddleName ?? string.Empty),
            Neutralise(contact.LastName ?? string.Empty),
            Neutralise(contact.Nickname ?? string.Empty),
            Neutralise(DisplayNameOf(contact)),
            Neutralise(contact.Organization ?? string.Empty),
            Neutralise(contact.Department ?? string.Empty),
            Neutralise(contact.JobTitle ?? string.Empty),
        ];
        for (var i = 0; i < addressColumns; i++)
            fields.Add(i < contact.Addresses.Count ? contact.Addresses[i].Address : string.Empty);
        fields.Add(Neutralise(contact.Notes ?? string.Empty));
        fields.Add(Neutralise(contact.Website ?? string.Empty));
        fields.Add(Neutralise(contact.Birthday ?? string.Empty));

        // First occurrence per bucket wins; a second CELL or a second HOME phone stays on the card
        // but never reaches the file — the CSV is not the card.
        var byBucket = new Dictionary<PhoneBucket, ContactDetailPhone>();
        foreach (var phone in contact.Phones) byBucket.TryAdd(BucketOf(phone.Type), phone);
        fields.AddRange(PhoneColumns.Select(c =>
            byBucket.TryGetValue(c.Bucket, out var phone) ? NeutralisePhone(phone.Number) : string.Empty));

        foreach (var (_, typeToken) in PostalGroups)
        {
            var address = contact.PostalAddresses
                .FirstOrDefault(a => a.Type.Contains(typeToken, StringComparison.OrdinalIgnoreCase));
            fields.AddRange(PostalFields.Select(f =>
                Neutralise(address == null ? string.Empty : f.Value(address) ?? string.Empty)));
        }

        fields.Add(contact.IsFavorite ? "true" : string.Empty);
        return fields;
    }

    private static PhoneBucket BucketOf(string type)
    {
        if (type.Contains("CELL", StringComparison.OrdinalIgnoreCase)) return PhoneBucket.Mobile;
        var isFax = type.Contains("FAX", StringComparison.OrdinalIgnoreCase);
        if (isFax && type.Contains("HOME", StringComparison.OrdinalIgnoreCase)) return PhoneBucket.HomeFax;
        if (isFax && type.Contains("WORK", StringComparison.OrdinalIgnoreCase)) return PhoneBucket.BusinessFax;
        if (type.Contains("HOME", StringComparison.OrdinalIgnoreCase)) return PhoneBucket.Home;
        if (type.Contains("WORK", StringComparison.OrdinalIgnoreCase)) return PhoneBucket.Business;
        return PhoneBucket.Other;
    }

    /// <summary>
    /// A leading apostrophe, which a spreadsheet eats on read, so a crafted value lands as text
    /// rather than as a formula. The apostrophe is a trigger itself, so
    /// <see cref="ContactCsvMapper"/>'s strip is symmetric on every column it reads back — the pair
    /// is lossless, a phone number legitimately opening on '+' included, and a value really
    /// beginning with one survives the round trip. E-mail addresses are the only field left out: a
    /// trigger-led address is forced into an unquoted local part, which always carries a mandatory
    /// <c>@</c> and non-empty domain — never a parseable formula in Excel or LibreOffice. A phone
    /// column is the other exemption, routed through <see cref="NeutralisePhone"/> instead: a
    /// plausible number carries no apostrophe either way, so the pair stays symmetric there too.
    /// </summary>
    private static string Neutralise(string field) =>
        field.Length > 0 && FormulaStarts.Contains(field[0]) ? $"'{field}" : field;

    // Every character in the value must be in the phone charset, not just the first — a value
    // opening on a legitimate '+' can still carry a DDE call past it. At least one digit keeps the
    // exemption to values that are actually phone-shaped, not just punctuation.
    private static bool IsPlausiblePhoneNumber(string field) =>
        field.Any(char.IsAsciiDigit) && field.All(c => char.IsAsciiDigit(c) || PhoneChars.Contains(c));

    private static string NeutralisePhone(string number) =>
        IsPlausiblePhoneNumber(number) ? number : Neutralise(number);

    /// <summary>
    /// Mirrors the frontend's displayNameOf, minus its address fallback: written verbatim, an
    /// address would come back as a nickname on the next import — a name nobody typed.
    /// </summary>
    private static string NameOf(ContactDetail contact)
    {
        var full = string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(n => n != null));
        return full.Length > 0 ? full : contact.Nickname ?? string.Empty;
    }

    /// <summary>
    /// The card's own FN wherever it has one — décision 10 stored <c>display_name</c> precisely so
    /// that <c>FN:Dr. John Smith Jr.</c> stops reading as "John Smith", and a file that flattens it
    /// back loses it user-visibly. One shape is excluded, and it is the one
    /// <see cref="NameOf"/> exists to refuse: <c>VCardComposer.FallbackDisplayName</c> ends on the
    /// first address, so an address-only contact's stored FN <em>is</em> that address, which the
    /// mapper reads back as a nickname. Compared ignoring case — the column is canonical, the
    /// card's own spelling need not be.
    /// </summary>
    private static string DisplayNameOf(ContactDetail contact) =>
        contact.DisplayName is { } display && !IsFirstAddress(contact, display)
            ? display
            : NameOf(contact);

    private static bool IsFirstAddress(ContactDetail contact, string value) =>
        contact.Addresses.FirstOrDefault() is { } first
        && string.Equals(first.Address, value, StringComparison.OrdinalIgnoreCase);

    // Deterministic order: the export has none of its own, and a file whose rows move between two
    // exports of an unchanged book is undiffable. The Id tiebreaker in Write matters here — two
    // contacts sharing a SortKey would otherwise keep the store's unspecified order between runs.
    private static string SortKey(ContactDetail contact)
    {
        var name = DisplayNameOf(contact);
        return name.Length > 0 ? name : contact.Addresses.FirstOrDefault()?.Address ?? string.Empty;
    }
}
