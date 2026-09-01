using System.Text;
using FolkerKinzel.VCards;
using FolkerKinzel.VCards.Enums;
using FolkerKinzel.VCards.Extensions;
using FolkerKinzel.VCards.Models;
using FolkerKinzel.VCards.Models.Properties;
using FolkerKinzel.VCards.Models.Properties.Parameters;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// The write half of the projection cycle: it replaces values inside the stored card, it never
/// rewrites a property (spec, décision 4). Group, PID, ALTID, LABEL and every X- parameter are
/// never read and never rebuilt; child lines are paired to card properties by rank. The library
/// serializer loses some of that on vCard 3.0, so <see cref="Emit"/> restores what a probe of
/// FolkerKinzel 8.2.0 proved dropped: X- parameters on EMAIL/TEL/URL/BDAY, occurrences past the
/// most preferred of the families its 3.0 writer collapses, every family no entry point models —
/// spliced back verbatim from the input card — and any BDAY the library refuses to spell as the
/// column does (décision 11).
/// </summary>
internal static class VCardComposer
{
    internal static readonly VcfOpts SerializationOptions = VcfOpts.Default
        .Set(VcfOpts.WriteNonStandardProperties)
        .Set(VcfOpts.WriteNonStandardParameters);

    private enum Family { Email, Phone, Postal }

    // Apple's dialect, the one a new group card is born in (décision 6).
    private const string GroupKindName = "X-ADDRESSBOOKSERVER-KIND";
    private const string GroupMemberName = "X-ADDRESSBOOKSERVER-MEMBER";

    // 101 is the erasure the projector reads back as no PREF at all (décision 5 bis of 4a). Measured
    // against FolkerKinzel 8.2.0's 3.0 and 4.0 writers — the two versions Emit produces — setting
    // Preference to 100 (its own default) makes both emit no PREF parameter or token at all.
    private const int NoPreference = 100;

    internal static string ComposeNew(string uid, ContactWrite write) =>
        Apply(SourceCard.Fresh(), uid, write);

    internal static string Compose(string existingCard, string uid, ContactWrite write) =>
        Apply(SourceCard.Read(existingCard), uid, write, RawBirthday(existingCard));

    internal static string Reconcile(string existingCard, string uid, ReconcileWrite write)
    {
        var source = SourceCard.Read(existingCard);
        var card = source.Card;
        // A reconciliation, not an edit: an empty column leaves the card's property alone —
        // deletion semantics belong to Compose, where a cleared field is the user clearing it.
        SetName(card, Components(write.FirstName), Components(write.LastName), null, null, null);
        // Off the reconciled N, never off the columns: the pre-4a FN was first + middle + last and
        // no column of that era held a middle name, so reading it from the write would flatten
        // "Jean Pierre Dupont" to "Jean Dupont" on the one pass that cannot be replayed.
        var name = (card.NameViews ?? []).FirstOrDefault(p => p is { IsEmpty: false })?.Value;
        var fallback = FallbackDisplayName(
            NamePart(name?.Given, name?.Given2), null, NamePart(name?.Surnames),
            write.Nickname, write.Addresses.FirstOrDefault());
        if (fallback.Length > 0) card.DisplayNames = ReplaceFirstText(card.DisplayNames, fallback);
        if (write.Nickname != null)
            card.NickNames = ReplaceFirstNickname(card.NickNames, write.Nickname);
        if (write.Addresses.Count > 0) ReplaceEmailBlock(card, write.Addresses);
        return Emit(source, uid, RawBirthday(existingCard));
    }

    internal static string MergeFill(string existingCard, string uid, MergeWrite write)
    {
        var source = SourceCard.Read(existingCard);
        var card = source.Card;
        SetName(card, Components(write.FirstName), Components(write.LastName),
            Components(write.MiddleName), Components(write.NamePrefix), Components(write.NameSuffix));
        if (write.Nickname != null)
            card.NickNames = ReplaceFirstNickname(card.NickNames, write.Nickname);
        if (!(card.DisplayNames ?? []).Any(p => p is { IsEmpty: false }))
            card.DisplayNames = ReplaceFirstText(card.DisplayNames,
                write.DisplayName ?? FallbackDisplayName(write.FirstName, write.MiddleName,
                    write.LastName, write.Nickname, write.AddedAddresses.FirstOrDefault()));
        PoseOptional(card, write.Organization, write.Department, write.JobTitle, write.Notes,
            write.Website, write.Birthday, write.Phones, write.PostalAddresses);
        var emails = (card.EMails ?? []).OfType<TextProperty>().ToList();
        emails.AddRange(write.AddedAddresses.Select(a => new TextProperty(a)));
        card.EMails = emails;
        return Emit(source, uid, write.Birthday ?? RawBirthday(existingCard));
    }

    // ---- groups (tranche 4e) --------------------------------------------------------------------

    /// <summary>A new group card — the only group write that goes through the serializer: a card
    /// that does not exist yet has nothing to preserve (décision 6). Born in 3.0, Apple's dialect.</summary>
    internal static string ComposeNewGroup(string uid, string name)
    {
        var card = new VCard
        {
            DisplayNames = [new TextProperty(name)],
            NonStandards = [new NonStandardProperty(GroupKindName, "group")],
        };
        return Emit(new SourceCard(card, VCdVersion.V3_0, [], [], null), uid, null);
    }

    // The three edits below rewrite one line of the stored card and copy the rest verbatim
    // (décision 6): a group card carries members the composer models nowhere, and the 3.0 writer
    // emits no MEMBER at all, so re-serializing one would empty it.
    internal static string AddGroupMember(string card, string memberUid)
    {
        var lines = LogicalLines(CanonicalLineBreaks(card));
        // The card's dialect, not ours: a mixed card is a memberless group to a strict 4.0 reader.
        var name = lines.Any(l => IsName(l, "KIND")) ? "MEMBER" : GroupMemberName;
        lines.Insert(EndIndex(lines), Fold($"{name}:{VCardProjector.UrnUuidPrefix}{memberUid}"));
        return Join(lines);
    }

    // Décision 7: the removal matches every value form the reading accepts — both names, the
    // urn:uuid: prefix optional and case-insensitive.
    internal static string RemoveGroupMember(string card, string memberUid)
    {
        var lines = LogicalLines(CanonicalLineBreaks(card));
        lines.RemoveAll(l =>
        {
            if (!IsName(l, "MEMBER") && !IsName(l, GroupMemberName)) return false;
            var unfolded = Unfold(l);
            var colon = IndexOutsideQuotes(unfolded, ':');
            return colon >= 0
                && VCardProjector.StripUrnUuid(unfolded[(colon + 1)..].Trim()) == memberUid;
        });
        return Join(lines);
    }

    internal static string RenameGroup(string card, string name)
    {
        var lines = LogicalLines(CanonicalLineBreaks(card));
        var escaped = EscapeText(name);
        // Valueless — a malformed FN carrying no colon at all — is no FN to rename (FirstRawLine's
        // own guard): the value replacement would eat the property name.
        var index = lines.FindIndex(l => IsName(l, "FN") && IndexOutsideQuotes(Unfold(l), ':') >= 0);
        if (index < 0)
        {
            lines.Insert(EndIndex(lines), Fold("FN:" + escaped));
            return Join(lines);
        }

        var unfolded = Unfold(lines[index]);
        var colon = IndexOutsideQuotes(unfolded, ':');
        lines[index] = Fold(unfolded[..(colon + 1)] + escaped);
        return Join(lines);
    }

    // Where a line joins the card: before END:VCARD, or at the end of a card that has no END.
    private static int EndIndex(List<string> lines)
    {
        var end = lines.FindLastIndex(l => IsName(l, "END"));
        return end < 0 ? lines.Count : end;
    }

    private static string Join(List<string> lines) => string.Join("\r\n", lines) + "\r\n";
    // The families and scalars an entry point poses only when its write names them — absent means
    // untouched here, whichever door came in.
    private static void PoseOptional(
        VCard card, string? organization, string? department, string? jobTitle, string? notes,
        string? website, string? birthday, IReadOnlyList<ContactWritePhone>? phones,
        IReadOnlyList<ContactWriteAddress>? postalAddresses)
    {
        if (organization != null || department != null)
            card.Organizations = ReplaceFirstOrg(card.Organizations, organization, department);
        if (jobTitle != null) card.Titles = ReplaceFirstText(card.Titles, jobTitle);
        if (notes != null) card.Notes = ReplaceFirstText(card.Notes, notes);
        if (website != null) card.Urls = ReplaceFirstText(card.Urls, website);
        if (birthday != null) card.BirthDayViews = ReplaceFirstBday(card.BirthDayViews, birthday);
        if (phones != null)
            card.Phones = Paired(card.Phones, phones, l => l.Position,
                (ContactWritePhone l, TextProperty? old) => TextLine(l.Number, l.Type, old, Family.Phone, l.Pref));
        if (postalAddresses != null)
            card.Addresses = Paired(card.Addresses, postalAddresses, l => l.Position, PostalLine);
    }

    private static string Apply(SourceCard source, string uid, ContactWrite write, string? rawBirthday = null)
    {
        var card = source.Card;
        // The names, the display name and the addresses are replaced, null included: there, null is
        // the user who emptied the box. On every other field null still means the request did not
        // name it and the card keeps its own — the editor clears those with an empty string.
        SetName(card,
            Components(write.FirstName) ?? [], Components(write.LastName) ?? [],
            Components(write.MiddleName), Components(write.NamePrefix), Components(write.NameSuffix));
        // The middle name read back off the recomposed N, never off the write: kept by the line
        // above, it would be missing from the FN of an edit that does not carry it (Reconcile's own
        // reason).
        var name = (card.NameViews ?? []).FirstOrDefault(p => p is { IsEmpty: false })?.Value;
        card.DisplayNames = ReplaceFirstText(card.DisplayNames,
            write.DisplayName ?? FallbackDisplayName(write.FirstName, NamePart(name?.Given2), write.LastName,
                write.Nickname, write.Addresses.Count > 0 ? write.Addresses[0].Address : null));
        card.NickNames = ReplaceFirstNickname(card.NickNames, write.Nickname);
        PoseOptional(card, write.Organization, write.Department, write.JobTitle, write.Notes,
            write.Website, write.Birthday, write.Phones, write.PostalAddresses);
        card.EMails = Paired(card.EMails, write.Addresses, l => l.Position,
            (ContactWriteEmail l, TextProperty? old) => TextLine(l.Address, l.Type, old, Family.Email, l.Pref));
        return Emit(source, uid, write.Birthday ?? rawBirthday);
    }

    /// <summary>
    /// The parsed card plus what the splice repairs need from the raw input: its logical lines,
    /// and — for the families the 3.0 writer collapses to one occurrence — the verbatim line of
    /// each parsed property, keyed by reference so untouched occurrences find their own bytes
    /// back after an edit. Both stay empty unless input and output are the same vCard 3.0 (a 2.1
    /// promotion re-encodes every line, so nothing of it may be spliced).
    /// </summary>
    private sealed record SourceCard(
        VCard Card, VCdVersion Version, List<string> InputChunks,
        Dictionary<VCardProperty, string> RawLineOf, string? RawUidLine)
    {
        internal static SourceCard Fresh() => new(new VCard(), VCdVersion.V3_0, [], [], null);

        internal static SourceCard Read(string existingCard)
        {
            VCard? parsed;
            try { parsed = Vcf.Parse(existingCard).FirstOrDefault(); }
            catch { parsed = null; }

            // Décision 7: the card's version, 2.1 promoted; an unreadable card starts over in 3.0.
            var version = parsed == null || parsed.Version == VCdVersion.V2_1
                ? VCdVersion.V3_0 : parsed.Version;
            // Read whatever the version: the UID repair is the one the 4.0 writer needs.
            var uidLine = RawUid(existingCard);
            if (parsed == null || parsed.Version != VCdVersion.V3_0)
                return new SourceCard(parsed ?? new VCard(), version, [], [], uidLine);

            // A lone \r is a line break to the parser too — left in place it would splice a card
            // boundary back in. And only the first card's lines may feed the splices.
            var all = LogicalLines(CanonicalLineBreaks(existingCard));
            var end = all.FindIndex(c => IsName(c, "END"));
            var chunks = end < 0 ? all : all.Take(end + 1).ToList();
            var rawOf = new Dictionary<VCardProperty, string>(ReferenceEqualityComparer.Instance);
            MapFamily(rawOf, chunks, "NICKNAME", parsed.NickNames);
            MapFamily(rawOf, chunks, "ORG", parsed.Organizations);
            MapFamily(rawOf, chunks, "TITLE", parsed.Titles);
            MapFamily(rawOf, chunks, "NOTE", parsed.Notes);
            MapFamily(rawOf, chunks, "URL", parsed.Urls);
            return new SourceCard(parsed, version, chunks, rawOf, uidLine);
        }

        // Raw lines pair with parsed properties by rank, only when the two counts agree.
        private static void MapFamily(
            Dictionary<VCardProperty, string> map, List<string> chunks, string name,
            IEnumerable<VCardProperty?>? properties)
        {
            var lines = chunks.Where(c => IsName(c, name)).ToList();
            var list = (properties ?? []).ToList();
            if (lines.Count != list.Count) return;
            for (var i = 0; i < list.Count; i++)
                if (list[i] is { } property)
                    map[property] = lines[i];
        }
    }

    // ---- names --------------------------------------------------------------------------------

    // null = keep the card's component (Reconcile/MergeFill bounds); a list = pose it. The comma
    // split is the inverse of the projector's join: a multi-valued component round-trips as one.
    private static string[]? Components(string? value) =>
        value == null ? null : value.Length == 0 ? [] : value.Split(',');

    private static void SetName(
        VCard card, string[]? given, string[]? surname, string[]? given2,
        string[]? prefixes, string[]? suffixes)
    {
        var old = (card.NameViews ?? []).FirstOrDefault(p => p is { IsEmpty: false });
        var builder = NameBuilder.Create();
        AddAll(v => builder.AddGiven(v), given ?? [.. old?.Value.Given ?? []]);
        AddAll(v => builder.AddSurname(v), surname ?? [.. old?.Value.Surnames ?? []]);
        AddAll(v => builder.AddGiven2(v), given2 ?? [.. old?.Value.Given2 ?? []]);
        AddAll(v => builder.AddPrefix(v), prefixes ?? [.. old?.Value.Prefixes ?? []]);
        AddAll(v => builder.AddSuffix(v), suffixes ?? [.. old?.Value.Suffixes ?? []]);
        AddAll(v => builder.AddSurname2(v), [.. old?.Value.Surnames2 ?? []]);
        AddAll(v => builder.AddGeneration(v), [.. old?.Value.Generations ?? []]);

        var replaced = new NameProperty(builder.Build(), old?.Group);
        if (old != null) replaced.Parameters.Assign(old.Parameters);
        card.NameViews = [replaced,
            .. (card.NameViews ?? []).OfType<NameProperty>().Where(p => !ReferenceEquals(p, old))];
    }

    private static void AddAll(Action<string> add, IReadOnlyList<string> values)
    {
        foreach (var value in values) add(value);
    }

    /// <summary>Name components as one display token, blanks dropped — the legacy FN's own join.</summary>
    private static string NamePart(params IEnumerable<string>?[] components) =>
        string.Join(' ', components.SelectMany(c => c ?? []).Where(v => !string.IsNullOrEmpty(v)));

    /// <summary>
    /// The FN a card gets when nobody typed one: its name components joined, then the nickname,
    /// then its first address. Internal because <see cref="VCardProjector"/> runs it in reverse —
    /// an FN this would have produced anyway is not a display name the user chose, and the column
    /// exists to hold the ones they did.
    /// </summary>
    internal static string FallbackDisplayName(
        string? first, string? middle, string? last, string? nickname, string? firstAddress)
    {
        var name = string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrEmpty(p)));
        return name.Length > 0 ? name : nickname ?? firstAddress ?? string.Empty;
    }

    // ---- scalars: replace the first occurrence, leave the others (décision 5) ------------------

    private static IEnumerable<TextProperty>? ReplaceFirstText(
        IEnumerable<TextProperty?>? properties, string? value)
    {
        var list = (properties ?? []).OfType<TextProperty>().ToList();
        if (string.IsNullOrEmpty(value)) return list.Count == 0 ? null : list.Skip(1).ToList();
        var old = list.FirstOrDefault();
        var replaced = new TextProperty(value, old?.Group);
        if (old != null) replaced.Parameters.Assign(old.Parameters);
        return [replaced, .. list.Skip(1)];
    }

    private static IEnumerable<StringCollectionProperty>? ReplaceFirstNickname(
        IEnumerable<StringCollectionProperty?>? properties, string? value)
    {
        var list = (properties ?? []).OfType<StringCollectionProperty>().ToList();
        if (string.IsNullOrEmpty(value)) return list.Count == 0 ? null : list.Skip(1).ToList();
        var old = list.FirstOrDefault();
        var replaced = new StringCollectionProperty(value.Split(','), old?.Group);
        if (old != null) replaced.Parameters.Assign(old.Parameters);
        return [replaced, .. list.Skip(1)];
    }

    private static IEnumerable<OrgProperty>? ReplaceFirstOrg(
        IEnumerable<OrgProperty?>? properties, string? organization, string? department)
    {
        var list = (properties ?? []).OfType<OrgProperty>().ToList();
        var old = list.FirstOrDefault();
        // The two halves share one line, so the one the write leaves absent is read back off the
        // card: rebuilding the line from the named half alone would erase the other.
        var name = Cleared(organization ?? old?.Value?.Name);
        var units = Cleared(department ?? Units(old?.Value));
        if (name == null && units == null)
            return list.Count == 0 ? null : list.Skip(1).ToList();
        var replaced = new OrgProperty(new Organization(name, units?.Split(';')), old?.Group);
        if (old != null) replaced.Parameters.Assign(old.Parameters);
        return [replaced, .. list.Skip(1)];
    }

    private static string? Cleared(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string? Units(Organization? organization) =>
        organization?.Units is { Count: > 0 } units ? string.Join(';', units) : null;

    private static IEnumerable<DateAndOrTimeProperty>? ReplaceFirstBday(
        IEnumerable<DateAndOrTimeProperty?>? properties, string? value)
    {
        var list = (properties ?? []).OfType<DateAndOrTimeProperty>().ToList();
        if (string.IsNullOrEmpty(value)) return list.Count == 0 ? null : list.Skip(1).ToList();
        var old = list.FirstOrDefault();
        var replaced = new DateAndOrTimeProperty(DateAndOrTime.Create(value), old?.Group);
        if (old != null) replaced.Parameters.Assign(old.Parameters);
        return [replaced, .. list.Skip(1)];
    }

    // ---- child lines: paired to card properties by rank (décision 4) ---------------------------

    private static List<TProp> Paired<TLine, TProp>(
        IEnumerable<TProp?>? existing, IReadOnlyList<TLine> lines,
        Func<TLine, int?> positionOf, Func<TLine, TProp?, TProp> build) where TProp : VCardProperty
    {
        var properties = (existing ?? []).ToList();
        var claims = new Dictionary<int, TLine>();
        var added = new List<TLine>();
        foreach (var line in lines)
        {
            // A position the card does not hold — or one already claimed — is a new line at the end.
            if (positionOf(line) is { } p && p >= 0 && p < properties.Count
                && properties[p] != null && claims.TryAdd(p, line))
                continue;
            added.Add(line);
        }

        var result = new List<TProp>();
        for (var i = 0; i < properties.Count; i++)
            if (claims.TryGetValue(i, out var line))
                result.Add(build(line, properties[i]));
        result.AddRange(added.Select(line => build(line, null)));
        return result;
    }

    private static TextProperty TextLine(string value, string type, TextProperty? old, Family family, int? pref)
    {
        var replaced = new TextProperty(value, old?.Group);
        if (old != null) replaced.Parameters.Assign(old.Parameters);
        ApplyType(replaced.Parameters, family, type);
        ApplyPreference(replaced.Parameters, pref);
        return replaced;
    }

    // The only place a write reaches PREF. ApplyType does not touch it — it strips the token from
    // the TYPE block so Preference has a single door; NoPreference's own comment carries the measurement.
    private static void ApplyPreference(ParameterSection parameters, int? pref)
    {
        if (pref == null) return;
        parameters.Preference = pref.Value >= 101 ? NoPreference : Math.Clamp(pref.Value, 1, 100);
    }

    private static AddressProperty PostalLine(ContactWriteAddress line, AddressProperty? old)
    {
        var builder = AddressBuilder.Create();
        AddAll(v => builder.AddPOBox(v), Components(line.PoBox) ?? []);
        AddAll(v => builder.AddExtended(v), Components(line.Extended) ?? []);
        AddAll(v => builder.AddStreet(v), Components(line.Street) ?? []);
        AddAll(v => builder.AddLocality(v), Components(line.Locality) ?? []);
        AddAll(v => builder.AddRegion(v), Components(line.Region) ?? []);
        AddAll(v => builder.AddPostalCode(v), Components(line.PostalCode) ?? []);
        AddAll(v => builder.AddCountry(v), Components(line.Country) ?? []);
        if (old?.Value is { } kept)
        {
            // The RFC 9554 components stay on the property, never read from the write (décision 4).
            AddAll(v => builder.AddRoom(v), kept.Room);
            AddAll(v => builder.AddApartment(v), kept.Apartment);
            AddAll(v => builder.AddFloor(v), kept.Floor);
            AddAll(v => builder.AddStreetNumber(v), kept.StreetNumber);
            AddAll(v => builder.AddStreetName(v), kept.StreetName);
            AddAll(v => builder.AddBuilding(v), kept.Building);
            AddAll(v => builder.AddBlock(v), kept.Block);
            AddAll(v => builder.AddSubDistrict(v), kept.SubDistrict);
            AddAll(v => builder.AddDistrict(v), kept.District);
            AddAll(v => builder.AddLandmark(v), kept.Landmark);
            AddAll(v => builder.AddDirection(v), kept.Direction);
        }

        var replaced = new AddressProperty(builder.Build(), old?.Group);
        if (old != null) replaced.Parameters.Assign(old.Parameters);
        ApplyType(replaced.Parameters, Family.Postal, line.Type);
        ApplyPreference(replaced.Parameters, line.Pref);
        return replaced;
    }

    // TYPE is replaced whole from the field, PREF excepted: Preference is left alone, so the 3.0
    // token and the 4.0 parameter both survive. Every other parameter of the block stays untouched.
    private static void ApplyType(ParameterSection parameters, Family family, string type)
    {
        parameters.PropertyClass = null;
        if (family == Family.Phone) parameters.PhoneType = null;
        if (family == Family.Email) parameters.EMailType = null;
        if (family == Family.Postal) parameters.AddressType = null;
        var kept = (parameters.NonStandard ?? [])
            .Where(p => !p.Key.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        // PREF is dropped — Preference already carries it and the projection echoes the token back
        // in the field; EMAIL's INTERNET too: the 3.0 writer adds it itself, 4.0 does not define it.
        var tokens = type
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !t.Equals("PREF", StringComparison.OrdinalIgnoreCase)
                && (family != Family.Email || !t.Equals("INTERNET", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (tokens.Count > 0) kept.Add(new("TYPE", string.Join(',', tokens)));
        parameters.NonStandard = kept.Count == 0 ? null : kept;
    }

    private static void ReplaceEmailBlock(VCard card, IReadOnlyList<string> addresses)
    {
        var properties = (card.EMails ?? []).ToList();
        var result = new List<TextProperty>();
        for (var i = 0; i < addresses.Count; i++)
        {
            var old = i < properties.Count ? properties[i] : null;
            var replaced = new TextProperty(addresses[i], old?.Group);
            if (old != null) replaced.Parameters.Assign(old.Parameters);
            result.Add(replaced);
        }

        card.EMails = result;
    }

    // ---- serialization and the 8.2.0 repairs ----------------------------------------------------

    private static string Emit(SourceCard source, string uid, string? birthday)
    {
        var card = source.Card;
        var old = card.ContactID;
        var id = new ContactIDProperty(ContactID.Create(uid), old?.Group);
        if (old != null) id.Parameters.Assign(old.Parameters);
        card.ContactID = id;

        var lines = LogicalLines(Serialize(card, source.Version));
        if (source.Version == VCdVersion.V3_0)
        {
            RestoreDroppedParameters(lines, card);
            SpliceCollapsedFamilies(lines, card, source);
            SpliceUnmodelledFamilies(lines, source);
        }

        StripNamePlaceholders(lines, card);
        EnforceBirthday(lines, card, birthday);
        RestoreUid(lines, source, uid);
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// The column's UID is a textual scan of the card (<see cref="VCardImportMapper.UidOf"/>) and
    /// never a value the composer transforms, so a card whose UID it did not touch keeps the line
    /// it arrived with: the 4.0 writer otherwise labels <c>VALUE=TEXT</c> anything it cannot read
    /// back as a URI, including the <c>urn:uuid:</c> form that is one.
    /// </summary>
    private static void RestoreUid(List<string> lines, SourceCard source, string uid)
    {
        if (source.RawUidLine is not { } raw) return;
        var colon = IndexOutsideQuotes(raw, ':');
        if (colon < 0 || raw[(colon + 1)..] != uid) return;
        var index = lines.FindIndex(c => IsName(c, "UID"));
        if (index >= 0) lines[index] = Fold(raw);
    }

    private static string Serialize(VCard card, VCdVersion version) =>
        Vcf.AsString([card], version, null, SerializationOptions);

    /// <summary>
    /// What FolkerKinzel 8.2.0's vCard 3.0 writer provably drops even with the two flags set: the
    /// non-standard parameters of TEL, EMAIL, URL and BDAY (its ADR and text-scalar builders do
    /// write them). Output lines are matched to model properties by rank, in the writer's own
    /// order — stable ascending on Preference — and any count mismatch degrades to no repair
    /// rather than a wrong one.
    /// </summary>
    private static void RestoreDroppedParameters(List<string> lines, VCard card)
    {
        Restore(lines, "EMAIL", Predicted(card.EMails));
        Restore(lines, "TEL", Predicted(card.Phones));
        Restore(lines, "URL", Predicted(card.Urls), firstOnly: true);
        Restore(lines, "BDAY", Predicted(card.BirthDayViews), firstOnly: true);
    }

    private static List<VCardProperty> Predicted(IEnumerable<VCardProperty?>? properties) =>
        [.. (properties ?? []).Where(p => p is { IsEmpty: false })
            .OrderBy(p => p!.Parameters.Preference).Cast<VCardProperty>()];

    private static void Restore(
        List<string> lines, string name, List<VCardProperty> predicted, bool firstOnly = false)
    {
        var indices = FamilyIndices(lines, name);
        if (firstOnly)
        {
            if (indices.Count > 0 && predicted.Count > 0)
                lines[indices[0]] = RestoreParams(lines[indices[0]], predicted[0]);
            return;
        }

        if (indices.Count != predicted.Count) return;
        for (var i = 0; i < indices.Count; i++)
            lines[indices[i]] = RestoreParams(lines[indices[i]], predicted[i]);
    }

    private static string RestoreParams(string chunk, VCardProperty property) =>
        AppendMissing(chunk, (property.Parameters.NonStandard ?? []).Select(p => $"{p.Key}={p.Value}"));

    // The candidates the line does not already carry, appended verbatim before its value.
    private static string AppendMissing(string chunk, IEnumerable<string> parameters)
    {
        var unfolded = Unfold(chunk);
        var colon = IndexOutsideQuotes(unfolded, ':');
        if (colon < 0) return chunk;
        var head = unfolded[..colon];
        var semi = IndexOutsideQuotes(head, ';');
        var block = semi < 0 ? string.Empty : head[(semi + 1)..];
        var missing = parameters
            .Where(p => p.Length > 0 && !HasParameter(block, KeyOf(p)))
            .Select(p => ";" + p)
            .ToList();
        return missing.Count == 0 ? chunk
            : Fold(head + string.Concat(missing) + unfolded[colon..]);
    }

    /// <summary>
    /// The 3.0 writer also emits only the most preferred occurrence of NICKNAME, ORG, TITLE, NOTE
    /// and URL. Décision 5 guarantees occurrences past the first are semantically untouched, so
    /// each one takes its own raw input line back, verbatim; the edited first occurrence keeps the
    /// writer's line. Any shape the mapping cannot vouch for is left exactly as the writer put it.
    /// </summary>
    private static void SpliceCollapsedFamilies(List<string> lines, VCard card, SourceCard source)
    {
        SpliceFamily(lines, "NICKNAME", card.NickNames, source,
            (solo, p) => solo.NickNames = [(StringCollectionProperty)p]);
        SpliceFamily(lines, "ORG", card.Organizations, source,
            (solo, p) => solo.Organizations = [(OrgProperty)p]);
        SpliceFamily(lines, "TITLE", card.Titles, source,
            (solo, p) => solo.Titles = [(TextProperty)p]);
        SpliceFamily(lines, "NOTE", card.Notes, source,
            (solo, p) => solo.Notes = [(TextProperty)p]);
        SpliceFamily(lines, "URL", card.Urls, source,
            (solo, p) => solo.Urls = [(TextProperty)p]);
    }

    private static void SpliceFamily(
        List<string> lines, string name, IEnumerable<VCardProperty?>? properties, SourceCard source,
        Action<VCard, VCardProperty> assignAlone)
    {
        var model = (properties ?? []).Where(p => p is { IsEmpty: false })
            .Cast<VCardProperty>().ToList();
        var indices = FamilyIndices(lines, name);
        // A family the edit brings down to one occurrence collapses to nothing at all for the
        // writer, which re-renders it alone and drops what its builders never re-emit — the 3.0 URL
        // builder writes no parameter whatsoever. The stored line of that rank still carries them:
        // the parameters come from it, the value from the model.
        if (model.Count == 1)
        {
            if (indices.Count == 1 && StoredLineOf(source, name, model[0]) is { } stored)
                lines[indices[0]] = AppendMissing(lines[indices[0]],
                    ParameterBlock(stored).Where(p => !DescribesTheValue(KeyOf(p))));
            return;
        }

        if (model.Count == 0 || indices.Count >= model.Count || indices.Count > 1) return;

        // Only rank 0 — the one occurrence the entry points ever edit — may lack a raw line. It is
        // re-rendered alone through the library, never taken from the writer's collapsed line:
        // that line is the *most preferred* occurrence, which a later PREF can make a different
        // property, and standing it in for the edit would discard the edit.
        var block = new List<string>();
        for (var i = 0; i < model.Count; i++)
        {
            if (source.RawLineOf.TryGetValue(model[i], out var raw)) block.Add(raw);
            else if (i == 0 && RenderAlone(name, model[0], assignAlone) is { } rendered) block.Add(rendered);
            else return;
        }

        var at = indices.Count == 1 ? indices[0] : lines.Count - 1;
        if (indices.Count == 1) lines.RemoveAt(indices[0]);
        lines.InsertRange(at, block);
    }

    /// <summary>
    /// The input line an occurrence stands on: its own when it survived the edit untouched, else —
    /// rank 0 being the only occurrence an entry point ever edits — the family's first input line.
    /// </summary>
    private static string? StoredLineOf(SourceCard source, string name, VCardProperty property) =>
        source.RawLineOf.TryGetValue(property, out var raw) ? raw
            : source.InputChunks.FirstOrDefault(c => IsName(c, name));

    // These three describe a value's own bytes or type, and on this path the value is the model's:
    // the one they described is gone. VALUE included — AppendMissing only reattaches what the
    // writer left out, so keeping it would re-label a value the composer did transform.
    private static bool DescribesTheValue(string key) =>
        key.Equals("ENCODING", StringComparison.OrdinalIgnoreCase)
        || key.Equals("CHARSET", StringComparison.OrdinalIgnoreCase)
        || key.Equals("VALUE", StringComparison.OrdinalIgnoreCase);

    private static List<string> ParameterBlock(string line)
    {
        var unfolded = Unfold(line);
        var colon = IndexOutsideQuotes(unfolded, ':');
        if (colon < 0) return [];
        var semi = IndexOutsideQuotes(unfolded[..colon], ';');
        return semi < 0 ? [] : SplitOutsideQuotes(unfolded[(semi + 1)..colon]);
    }

    internal static string KeyOf(string parameter) =>
        (parameter.IndexOf('=') is var eq && eq < 0 ? parameter : parameter[..eq]).Trim();

    // The library's own rendering of a single property: a solo card cannot collapse anything, and
    // the parameter repair still applies (the 3.0 URL builder writes no parameters at all).
    private static string? RenderAlone(
        string name, VCardProperty property, Action<VCard, VCardProperty> assignAlone)
    {
        var solo = new VCard();
        assignAlone(solo, property);
        var rendered = LogicalLines(Serialize(solo, VCdVersion.V3_0));
        var indices = FamilyIndices(rendered, name);
        return indices.Count == 1 ? RestoreParams(rendered[indices[0]], property) : null;
    }

    // Every name no entry point edits. The writer owns the rest: LABEL lines are derived from ADR
    // parameters and AGENT can nest a whole card, so neither may be spliced from the input.
    private static readonly HashSet<string> OwnedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "END", "VERSION", "PRODID", "REV", "UID", "N", "FN", "NICKNAME", "ORG", "TITLE",
        "NOTE", "URL", "BDAY", "EMAIL", "TEL", "ADR", "LABEL", "AGENT",
    };

    /// <summary>
    /// Décision 4, taken literally for everything the composer does not model: PHOTO, KEY, IMPP,
    /// CATEGORIES, GEO, the X- families… were not edited, so their input lines replace whatever
    /// the writer made of them — verbatim bytes, groups, X- parameters and occurrences included.
    /// </summary>
    private static void SpliceUnmodelledFamilies(List<string> lines, SourceCard source)
    {
        var families = new List<string>();
        var inputLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in source.InputChunks)
        {
            var name = NameOf(Unfold(chunk));
            if (name.Length == 0 || OwnedNames.Contains(name)) continue;
            if (!inputLines.TryGetValue(name, out var group))
            {
                inputLines[name] = group = [];
                families.Add(name);
            }

            group.Add(chunk);
        }

        foreach (var name in families)
        {
            var indices = FamilyIndices(lines, name);
            var at = indices.Count > 0 ? indices[0] : lines.Count - 1;
            for (var i = indices.Count - 1; i >= 0; i--) lines.RemoveAt(indices[i]);
            lines.InsertRange(at, inputLines[name]);
        }
    }

    private static List<int> FamilyIndices(List<string> lines, string name)
    {
        var indices = new List<int>();
        for (var i = 0; i < lines.Count; i++)
            if (IsName(lines[i], name)) indices.Add(i);
        return indices;
    }

    private static bool HasParameter(string block, string key) =>
        SplitOutsideQuotes(block).Any(p => KeyOf(p).Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// FolkerKinzel 8.2.0 fills a nameless card's mandatory N (vCard 3.0 only) and FN (both
    /// versions) with a question mark, which the total projection then reads back as a name.
    /// Repaired to the RFC 2426 empty forms, and only where the emptiness is ours: a card that
    /// genuinely declares <c>N:?;;;;</c> is not ours to rewrite (décision 1) — the projector's
    /// guard is what keeps that one out of the columns.
    /// </summary>
    internal static void StripNamePlaceholders(List<string> lines, VCard card)
    {
        if (Nameless(card.NameViews)) Blank(lines, "N", ";;;;");
        if (Nameless(card.DisplayNames)) Blank(lines, "FN", string.Empty);
    }

    private static bool Nameless(IEnumerable<VCardProperty?>? properties) =>
        !(properties ?? []).Any(p => p is { IsEmpty: false });

    // Only a value holding nothing but the placeholder and its component separators is replaced,
    // so a writer that stops filling the blank leaves the line exactly as it put it.
    private static void Blank(List<string> lines, string name, string empty)
    {
        var index = lines.FindIndex(c => IsName(c, name));
        if (index < 0) return;
        var unfolded = Unfold(lines[index]);
        var colon = IndexOutsideQuotes(unfolded, ':');
        if (colon < 0 || !unfolded[(colon + 1)..].All(c => c is '?' or ';')) return;
        lines[index] = Fold(unfolded[..(colon + 1)] + empty);
    }

    /// <summary>
    /// Décision 11: the column's spelling is what the card carries, whatever the version. The
    /// library corrupts a partial date in 3.0 (<c>--0315</c> gains year 4), drops a text form
    /// outright, or re-labels it VALUE=TEXT in 4.0 — whenever the emitted value is not the
    /// column's, the whole BDAY line is re-emitted verbatim with the property's X- parameters.
    /// </summary>
    private static void EnforceBirthday(List<string> lines, VCard card, string? birthday)
    {
        if (string.IsNullOrEmpty(birthday)) return;
        var index = -1;
        for (var i = 0; i < lines.Count && index < 0; i++)
            if (IsName(lines[i], "BDAY")) index = i;

        if (index >= 0)
        {
            var unfolded = Unfold(lines[index]);
            var colon = IndexOutsideQuotes(unfolded, ':');
            var relabelled = unfolded[..Math.Max(colon, 0)]
                .Contains("VALUE=TEXT", StringComparison.OrdinalIgnoreCase);
            if (colon >= 0 && unfolded[(colon + 1)..] == birthday && !relabelled) return;
        }

        var property = (card.BirthDayViews ?? []).FirstOrDefault(p => p != null);
        var line = BuildLine("BDAY", property, EscapeLineBreaks(birthday));
        if (index >= 0) lines[index] = line;
        else lines.Insert(lines.Count - 1, line); // before END:VCARD
    }

    private static string BuildLine(string name, VCardProperty? property, string value)
    {
        var builder = new StringBuilder();
        if (property?.Group is { Length: > 0 } group) builder.Append(group).Append('.');
        builder.Append(name);
        foreach (var pair in property?.Parameters.NonStandard ?? [])
            builder.Append(';').Append(pair.Key).Append('=').Append(pair.Value);
        return Fold(builder.Append(':').Append(value).ToString());
    }

    // ---- raw-text primitives --------------------------------------------------------------------

    /// <summary>Bare LF or CR spelled as CRLF, so every reader here sees the same lines.</summary>
    internal static string CanonicalLineBreaks(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

    internal static List<string> LogicalLines(string text)
    {
        var lines = new List<string>();
        foreach (var physical in text.Split("\r\n"))
        {
            if (physical.Length == 0) continue;
            if ((physical[0] == ' ' || physical[0] == '\t') && lines.Count > 0)
                lines[^1] += "\r\n" + physical;
            else
                lines.Add(physical);
        }

        return lines;
    }

    internal static string Unfold(string chunk) =>
        chunk.Replace("\r\n ", string.Empty).Replace("\r\n\t", string.Empty);

    internal static string Fold(string line)
    {
        if (line.Length <= 75) return line;
        var cut = CutAt(line, 75);
        var builder = new StringBuilder().Append(line, 0, cut);
        while (cut < line.Length)
        {
            var next = CutAt(line, Math.Min(cut + 74, line.Length));
            builder.Append("\r\n ").Append(line, cut, next - cut);
            cut = next;
        }

        return builder.ToString();
    }

    // Fold counts UTF-16 units where RFC 6350 counts octets, a divergence the residuals record and
    // this does not close. Cutting between the halves of a surrogate pair is the part that must
    // close: an over-long line is tolerated everywhere, invalid UTF-8 nowhere.
    private static int CutAt(string line, int index) =>
        index < line.Length && char.IsHighSurrogate(line[index - 1]) ? index - 1 : index;

    // The property name of an unfolded line, its group prefix stripped — "" when not a property.
    internal static string NameOf(string line)
    {
        var start = 0;
        var end = line.IndexOfAny([';', ':', '.']);
        if (end >= 0 && line[end] == '.')
        {
            start = end + 1;
            var next = line[start..].IndexOfAny([';', ':']);
            end = next < 0 ? -1 : start + next;
        }

        return end < 0 ? string.Empty : line[start..end];
    }

    // Whether an unfolded-or-folded line carries one property name, its group prefix aside.
    internal static bool IsName(string chunk, string name) =>
        NameOf(Unfold(chunk)).Equals(name, StringComparison.OrdinalIgnoreCase);

    internal static int IndexOutsideQuotes(string text, char target)
    {
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') inQuotes = !inQuotes;
            else if (!inQuotes && text[i] == target) return i;
        }

        return -1;
    }

    internal static List<string> SplitOutsideQuotes(string parameters, char separator = ';')
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] == '"') inQuotes = !inQuotes;
            else if (parameters[i] == separator && !inQuotes)
            {
                parts.Add(parameters[start..i]);
                start = i + 1;
            }
        }

        if (start < parameters.Length) parts.Add(parameters[start..]);
        return parts;
    }

    // The first occurrence of one property in the stored card, unfolded and never past END:VCARD.
    private static string? FirstRawLine(string vcardRaw, string name)
    {
        foreach (var logical in LogicalLines(CanonicalLineBreaks(vcardRaw)))
        {
            var line = Unfold(logical);
            var found = NameOf(line);
            if (found.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
            if (found.Equals(name, StringComparison.OrdinalIgnoreCase) && IndexOutsideQuotes(line, ':') >= 0)
                return line;
        }

        return null;
    }

    /// <summary>The first raw value of one property, as the card writes it — never decoded.</summary>
    internal static string? FirstRawValue(string vcardRaw, string name) =>
        FirstRawLine(vcardRaw, name) is { } line ? line[(IndexOutsideQuotes(line, ':') + 1)..] : null;

    // The first BDAY's raw value in the stored card — what Reconcile and MergeFill, which carry no
    // birthday of their own, must keep the card spelling through re-serialization (décision 11).
    private static string? RawBirthday(string vcardRaw) =>
        FirstRawLine(vcardRaw, "BDAY") is { } line
            ? line[(IndexOutsideQuotes(line, ':') + 1)..] : null;

    internal static string? RawUid(string vcardRaw) => FirstRawLine(vcardRaw, "UID");

    // The price of writing a line by hand is escaping it by hand (décision 6): the backslash
    // first, or it re-escapes the escapes it has just posed.
    internal static string EscapeText(string value) => EscapeLineBreaks(value.Replace("\\", "\\\\"))
        .Replace(";", "\\;").Replace(",", "\\,");

    private static string EscapeLineBreaks(string value) =>
        value.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");
}
