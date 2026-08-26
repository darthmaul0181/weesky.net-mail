using System.Text;
using FolkerKinzel.VCards;
using FolkerKinzel.VCards.Enums;
using FolkerKinzel.VCards.Formatters;
using FolkerKinzel.VCards.Models.Properties;
using FolkerKinzel.VCards.Models.Properties.Parameters;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// The read half of the projection cycle: stored card in, the columns' content out. Pure and
/// total — an unreadable card yields an empty projection, never an exception (spec, décision 8).
/// Decoded values come from FolkerKinzel; <c>Params</c> (and the TYPE and PREF it carries) comes
/// verbatim from the raw text, aligned with the library's collections by document rank.
/// </summary>
internal static class VCardProjector
{
    // Column widths ContactValidator does not mirror: display_name 255, uid 255, group_name 64,
    // params 255 (emails/phones) but 512 (addresses), and the seven contact_addresses components.
    private const int MaxDisplayNameLength = 255;

    /// <summary>What <c>contacts.uid</c> holds — the import refuses a card whose UID is longer.</summary>
    internal const int MaxUidLength = 255;

    private const int MaxGroupLength = 64;
    private const int MaxLineParamsLength = 255;
    private const int MaxAddressParamsLength = 512;
    private const int MaxPoBoxLength = 64;
    private const int MaxExtendedLength = 255;
    private const int MaxStreetLength = 255;
    private const int MaxLocalityLength = 128;
    private const int MaxRegionLength = 128;
    private const int MaxPostalCodeLength = 32;
    private const int MaxCountryLength = 128;

    // What a writer puts in a mandatory N or FN it has nothing to fill.
    private const string Placeholder = "?";

    private static readonly ContactProjection Empty = new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        [], [], [], null);

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static ContactProjection Project(string vcardRaw)
    {
        VCard? card;
        try { card = Vcf.Parse(vcardRaw).FirstOrDefault(); }
        catch { card = null; }
        if (card == null) return Empty;

        var raw = new RawCard(vcardRaw);
        var name = First(card.NameViews)?.Value;
        var org = First(card.Organizations)?.Value;
        var emails = Emails(card, raw);

        var first = NamePart(name?.Given, ContactValidator.MaxNameLength);
        var last = NamePart(name?.Surnames, ContactValidator.MaxNameLength);
        var middle = NamePart(name?.Given2, ContactValidator.MaxMiddleNameLength);
        var nickname = Joined(First(card.NickNames)?.Value, ContactValidator.MaxNameLength);

        return new ContactProjection(
            first,
            last,
            nickname,
            Chosen(Scalar(WithoutPlaceholder(First(card.DisplayNames)?.Value), MaxDisplayNameLength),
                first, middle, last, nickname, emails.FirstOrDefault()?.Address),
            middle,
            NamePart(name?.Prefixes, ContactValidator.MaxNamePartLength),
            NamePart(name?.Suffixes, ContactValidator.MaxNamePartLength),
            Scalar(org?.Name, ContactValidator.MaxOrganizationLength),
            Scalar(org?.Units == null ? null : string.Join(';', org.Units), ContactValidator.MaxOrganizationLength),
            Scalar(First(card.Titles)?.Value, ContactValidator.MaxOrganizationLength),
            Scalar(raw.Birthday, ContactValidator.MaxBirthdayLength),
            Scalar(First(card.Urls)?.Value, ContactValidator.MaxWebsiteLength),
            Scalar(First(card.Notes)?.Value, ContactValidator.MaxNotesLength),
            Scalar(Uid(card), MaxUidLength),
            emails,
            Phones(card, raw),
            PostalAddresses(card, raw),
            Photo(card));
    }

    private static List<ProjectedEmail> Emails(VCard card, RawCard raw)
    {
        var props = (card.EMails ?? []).ToList();
        var aligned = raw.CountOf(RawCard.Email) == props.Count;
        var emails = new List<ProjectedEmail>();
        var rank = 0;
        foreach (var prop in props)
        {
            var position = rank++;
            var address = (prop?.Value ?? string.Empty).Trim();
            // The named exception to décision 8: an address is dropped whole, never truncated —
            // a cut address is a wrong recipient, not a degraded value. Card ranks survive as is.
            if (prop == null || address.Length == 0 || address.Length > ContactValidator.MaxAddressLength
                || !ContactValidator.IsValidAddress(address))
                continue;
            emails.Add(new ProjectedEmail(
                IdentityResolver.Canonical(address),
                Line(position, prop, aligned ? raw.ParamsOf(RawCard.Email, position) : null, MaxLineParamsLength)));
        }

        return emails;
    }

    private static List<ProjectedPhone> Phones(VCard card, RawCard raw)
    {
        var props = (card.Phones ?? []).ToList();
        var aligned = raw.CountOf(RawCard.Tel) == props.Count;
        var phones = new List<ProjectedPhone>();
        var rank = 0;
        foreach (var prop in props)
        {
            var position = rank++;
            if (prop == null) continue;
            phones.Add(new ProjectedPhone(
                Truncate(prop.Value ?? string.Empty, ContactValidator.MaxPhoneNumberLength),
                Line(position, prop, aligned ? raw.ParamsOf(RawCard.Tel, position) : null, MaxLineParamsLength)));
        }

        return phones;
    }

    private static List<ProjectedAddress> PostalAddresses(VCard card, RawCard raw)
    {
        var props = (card.Addresses ?? []).ToList();
        var aligned = raw.CountOf(RawCard.Adr) == props.Count;
        var addresses = new List<ProjectedAddress>();
        var rank = 0;
        foreach (var prop in props)
        {
            var position = rank++;
            if (prop == null) continue;
            var value = prop.Value;
            addresses.Add(new ProjectedAddress(
                Joined(value.POBox, MaxPoBoxLength),
                Joined(value.Extended, MaxExtendedLength),
                HasRfc9554Components(value) ? null : Joined(value.Street, MaxStreetLength),
                Joined(value.Locality, MaxLocalityLength),
                Joined(value.Region, MaxRegionLength),
                Joined(value.PostalCode, MaxPostalCodeLength),
                Joined(value.Country, MaxCountryLength),
                Line(position, prop, aligned ? raw.ParamsOf(RawCard.Adr, position) : null, MaxAddressParamsLength)));
        }

        return addresses;
    }

    private static ProjectedPhoto? Photo(VCard card)
    {
        // Décision 12: the first PHOTO occurrence is the card's primary photo, projected only if
        // it is embedded (a data: URI in 4.0, ENCODING=b in 3.0 — both surface as bytes) and a
        // raster image. An http(s) or otherwise inadmissible first PHOTO projects nothing at all,
        // even when a later one would qualify; the URI is never fetched (SSRF).
        var prop = First(card.Photos);
        if (prop?.Value.Bytes is not { Length: > 0 } bytes) return null;
        return SniffRasterType(bytes) is { } mediaType ? new ProjectedPhoto(mediaType, bytes) : null;
    }

    // Décision 12: only raster images are served; an SVG is executable XML, not an avatar. The
    // bytes decide the type — neither the TYPE parameter nor the data: URI is trustworthy, and
    // the stored media_type is served as Content-Type later. Unrecognised bytes project nothing.
    private static string? SniffRasterType(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? "image/jpeg"
        : bytes.AsSpan().StartsWith(PngSignature) ? "image/png"
        : bytes.AsSpan().StartsWith("GIF8"u8) ? "image/gif"
        : bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
            ? "image/webp"
        : null;

    // RFC 9554 corollary of décision 4: when any component beyond the seven of RFC 6350 is
    // present, "street" is a duplicate of their combined value and the projection ignores it.
    private static bool HasRfc9554Components(FolkerKinzel.VCards.Models.Address value)
    {
        ICompoundModel components = value;
        for (var i = 7; i < components.Count; i++)
            if (components[i].Any(c => c.Length > 0)) return true;
        return false;
    }

    private static ProjectedLine Line(int position, VCardProperty prop, string? rawParams, int paramsWidth)
    {
        // rawParams is null when the raw scanner and the library disagree on ranks (nested 2.1
        // AGENT, quoted-printable folds): degrade to the library's parsed view rather than passing
        // a desync off as "no parameters" — Type and Pref are semantic columns, not display-only.
        if (rawParams == null)
            return new ProjectedLine(
                position, FallbackType(prop.Parameters), Math.Clamp(prop.Parameters.Preference, 1, 100),
                string.Empty, Truncate(prop.Group ?? string.Empty, MaxGroupLength));

        var (type, hasPrefParam, hasPrefToken) = ScanParams(rawParams);
        // Décision 5 bis. The library defaults Preference to 100 when the card says nothing, so
        // presence is judged on the raw params: PREF= → the parsed value, TYPE=..,PREF → 1, else 101.
        var pref = hasPrefParam ? Math.Clamp(prop.Parameters.Preference, 1, 100)
            : hasPrefToken ? 1 : 101;
        return new ProjectedLine(
            position,
            Truncate(type, ContactValidator.MaxTypeLength),
            pref,
            TruncateParams(rawParams, paramsWidth),
            Truncate(prop.Group ?? string.Empty, MaxGroupLength));
    }

    private static (string Type, bool HasPrefParam, bool HasPrefToken) ScanParams(string rawParams)
    {
        var type = new StringBuilder();
        var hasPrefParam = false;
        var hasPrefToken = false;
        foreach (var parameter in SplitOutsideQuotes(rawParams))
        {
            var eq = parameter.IndexOf('=');
            var parameterName = (eq < 0 ? parameter : parameter[..eq]).Trim();
            if (parameterName.Equals("PREF", StringComparison.OrdinalIgnoreCase))
            {
                hasPrefParam = true;
            }
            else if (parameterName.Equals("TYPE", StringComparison.OrdinalIgnoreCase) && eq >= 0)
            {
                var value = Unquote(parameter[(eq + 1)..]);
                if (type.Length > 0) type.Append(',');
                type.Append(value);
                hasPrefToken |= value.Split(',').Any(t => t.Trim().Equals("PREF", StringComparison.OrdinalIgnoreCase));
            }
        }

        return (type.ToString(), hasPrefParam, hasPrefToken);
    }

    // Truncation on a ';' boundary outside quotes: the first parameter that does not fit whole is
    // dropped with everything after it — a display column never shows half a parameter (décision 8).
    private static string TruncateParams(string rawParams, int width)
    {
        if (rawParams.Length <= width) return rawParams;
        var kept = 0;
        foreach (var parameter in SplitOutsideQuotes(rawParams))
        {
            var next = kept == 0 ? parameter.Length : kept + 1 + parameter.Length;
            if (next > width) break;
            kept = next;
        }

        return rawParams[..kept];
    }

    private static List<string> SplitOutsideQuotes(string parameters)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] == '"') inQuotes = !inQuotes;
            else if (parameters[i] == ';' && !inQuotes)
            {
                parts.Add(parameters[start..i]);
                start = i + 1;
            }
        }

        if (start < parameters.Length) parts.Add(parameters[start..]);
        return parts;
    }

    private static string FallbackType(ParameterSection parameters)
    {
        if (parameters.PropertyClass is not { } cls) return string.Empty;
        var parts = new List<string>(2);
        if (cls.HasFlag(PCl.Home)) parts.Add("HOME");
        if (cls.HasFlag(PCl.Work)) parts.Add("WORK");
        return string.Join(',', parts);
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static string? Uid(VCard card) =>
        card.ContactID?.Value is { } id
            ? id.String ?? id.Uri?.OriginalString ?? id.Guid?.ToString()
            : null;

    // N and FN are mandatory, and more than one writer fills an empty one with a question mark —
    // ours did, until the composer's repair. A card stored verbatim keeps it (décision 1), so the
    // guard is here, and it drops the placeholder component by component: a "?" beside a real name
    // would otherwise reach the column and put the character back on the tile.
    private static string? NamePart(IReadOnlyList<string>? values, int width) =>
        values != null && values.Any(v => v.Length > 0 && v != Placeholder)
            ? Joined(values, width) : null;

    /// <summary>
    /// The FN the user chose, or null when the card only carries the one a writer computes.
    ///
    /// Every card has an FN — vCard 3.0 and 4.0 both make it mandatory, and
    /// <see cref="VCardComposer.FallbackDisplayName"/> fills it whenever a write names none. So
    /// projecting it verbatim made <c>display_name</c> non-null on every contact the store ever
    /// created, and the editor then echoed that value back on the next save: the FN froze at the
    /// shape the name had on the day the card was made, and a later rename never reached it.
    ///
    /// Reading it back through the rule that wrote it separates the two. What survives is a
    /// display name that says something the components do not — <c>FN:Dr. John Smith Jr.</c> off
    /// an import — and that is what the column was added to preserve. The card keeps its FN
    /// either way; only the column goes empty, which is what makes the editor's box empty and
    /// lets <c>Apply</c> recompute the FN from the names on every save.
    ///
    /// The address arm compares case-blind because <see cref="Emails"/> canonicalises and the FN
    /// does not: a nameless card's FN <em>is</em> its first address, uppercase and all.
    /// </summary>
    private static string? Chosen(
        string? displayName, string? first, string? middle, string? last,
        string? nickname, string? firstAddress)
    {
        if (displayName == null) return null;
        var named = VCardComposer.FallbackDisplayName(first, middle, last, nickname, null);
        var derived = named.Length > 0
            ? displayName == named
            : firstAddress != null
              && string.Equals(displayName, firstAddress, StringComparison.OrdinalIgnoreCase);
        return derived ? null : displayName;
    }

    private static string? WithoutPlaceholder(string? value) =>
        value == Placeholder ? null : value;

    private static T? First<T>(IEnumerable<T?>? properties) where T : VCardProperty =>
        properties?.FirstOrDefault(p => p != null);

    // Multi-valued components are stored joined, never reduced to their first value (décision 4).
    private static string? Joined(IReadOnlyList<string>? values, int width) =>
        values == null || values.Count == 0 ? null : Scalar(string.Join(',', values), width);

    private static string? Scalar(string? value, int width) =>
        NullIfEmpty(value) == null ? null : Truncate(value!, width);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..width];

    /// <summary>
    /// The raw-text side of the projection: one unfolding, one pass over the lines, and per family
    /// the verbatim parameter block of each property in document order — what separates the first
    /// ';' from the first ':' outside quotes, no decoding. BDAY's value is kept as written too,
    /// because the library types the date and the column stores the card's own form (décision 11).
    /// </summary>
    private sealed class RawCard
    {
        internal const int Email = 0;
        internal const int Tel = 1;
        internal const int Adr = 2;
        private const int Bday = 3;
        private const int Begin = 4;
        private const int End = 5;

        private static readonly string[] Families = ["EMAIL", "TEL", "ADR", "BDAY", "BEGIN", "END"];

        private readonly List<string>[] parameters = [[], [], []];

        internal string? Birthday { get; }

        internal RawCard(string vcardRaw)
        {
            var unfolded = vcardRaw.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");
            var text = unfolded.AsSpan();
            var depth = 0;
            foreach (var range in text.Split('\n'))
            {
                var line = text[range];
                var family = FamilyOf(line, out var afterName);
                if (family < 0) continue;
                // The library reads the first card only, so nothing past it may reach these ranks.
                // Depth, not the first END:VCARD: a 2.1 AGENT embeds a whole card, whose lines are
                // the outer card's own text and are what the alignment guard exists to notice.
                if (family == Begin)
                {
                    depth++;
                    continue;
                }

                if (family == End)
                {
                    if (--depth <= 0) break;
                    continue;
                }

                var semi = -1;
                var colon = -1;
                var inQuotes = false;
                for (var i = afterName; i < line.Length && colon < 0; i++)
                {
                    if (line[i] == '"') inQuotes = !inQuotes;
                    else if (!inQuotes && line[i] == ':') colon = i;
                    else if (!inQuotes && line[i] == ';' && semi < 0) semi = i;
                }

                if (colon < 0) continue; // not a property line; the library skips it too
                if (family == Bday) Birthday ??= line[(colon + 1)..].ToString();
                else parameters[family].Add(semi < 0 ? string.Empty : line[(semi + 1)..colon].ToString());
            }
        }

        internal int CountOf(int family) => parameters[family].Count;

        // Null past the scanned list — a desync must surface as such, never as "no parameters".
        internal string? ParamsOf(int family, int rank) =>
            rank < parameters[family].Count ? parameters[family][rank] : null;

        // Matches ^(group.)?NAME, case-insensitive, NAME immediately followed by ';' or ':'.
        private static int FamilyOf(ReadOnlySpan<char> line, out int afterName)
        {
            afterName = 0;
            var start = 0;
            var end = line.IndexOfAny(';', ':', '.');
            if (end >= 0 && line[end] == '.')
            {
                start = end + 1;
                var next = line[start..].IndexOfAny(';', ':');
                end = next < 0 ? -1 : start + next;
            }

            if (end < 0) return -1;
            afterName = end;
            var name = line[start..end];
            for (var f = 0; f < Families.Length; f++)
                if (name.Equals(Families[f], StringComparison.OrdinalIgnoreCase))
                    return f;
            return -1;
        }
    }
}
