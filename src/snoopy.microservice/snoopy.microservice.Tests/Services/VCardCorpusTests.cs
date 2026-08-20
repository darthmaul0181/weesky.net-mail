using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The corpus of § Les tests: cards in the documented shapes of four real clients — iPhone 3.0 with
/// its <c>item1.</c> groups, Google 3.0, Thunderbird 102+ 4.0, DAVx⁵ 4.0 with RFC 9554 components.
/// Anonymous by construction (fictional names, <c>exemple.example</c>, <c>+3247…</c>).
/// <para>
/// Replacing a file with a real anonymised export under the same name is the intended path, and it
/// splits the suite in two. The two theories — <c>Corpus_ProjectsEveryCard</c> and
/// <c>Corpus_SurvivesASingleFieldEdit</c> — are corpus-agnostic: they assert invariants of the
/// engine and hold on any card. Every <c>[Fact]</c> here, and the pins, are bound to the shipped
/// corpus: they carry its literals — names, counts, UIDs, whole lines byte for byte — and they
/// <b>will fail loudly on replacement</b>. That is the intended behaviour, not a defect: the new
/// export's own values are what the reader must then re-observe and re-pin, which is the whole
/// point of a fact written against a real card.
/// </para>
/// </summary>
public sealed class VCardCorpusTests
{
    private const string Uid = "corpus-uid";

    public static TheoryData<string> AllClients() =>
        ["iphone.vcf", "google.vcf", "thunderbird.vcf", "davx5.vcf"];

    // ---- projection ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllClients))]
    public void Corpus_ProjectsEveryCard(string file)
    {
        var chunks = VCardSplitter.Split(Fixture(file));
        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.True(VCardSplitter.IsComplete(chunk));
            var p = VCardProjector.Project(chunk.Text);
            Assert.NotNull(p.DisplayName); // FN is mandatory in all four
            Assert.All(p.Addresses, a => Assert.True(ContactValidator.IsValidAddress(a.Address)));
            Assert.All(p.Addresses, a => Assert.InRange(a.Line.Pref, 1, 101));
            Assert.All(p.Phones, t => Assert.NotEqual(string.Empty, t.Number));
        }
    }

    [Fact] // iPhone: the item groups bind labels to properties, and TYPE=pref is the 3.0 preferred mark
    public void Iphone_ProjectsGroupsPrefAndEmbeddedPhoto()
    {
        var p = VCardProjector.Project(Card("iphone.vcf", 0));

        Assert.Equal("Prenom Test", p.DisplayName);
        Assert.Equal("Marie", p.MiddleName);
        Assert.Equal("Support technique", p.Department);
        Assert.Equal("item1", p.Addresses[0].Line.GroupName);
        Assert.Equal(1, p.Addresses[0].Line.Pref);           // type=pref → 1 (décision 5 bis)
        Assert.Equal(101, p.Addresses[1].Line.Pref);         // nothing said → 101
        Assert.Equal("item2", p.Phones[1].Line.GroupName);
        Assert.Equal("item3", p.PostalAddresses[0].Line.GroupName);
        Assert.Equal("Rue Neuve 1", p.PostalAddresses[0].Street);
        Assert.Equal("image/jpeg", p.Photo?.MediaType);      // ENCODING=b, sniffed from the bytes
        Assert.Equal("1604-03-15", p.Birthday);              // X-APPLE-OMIT-YEAR, value kept verbatim
    }

    [Fact] // Google: an http(s) PHOTO is never fetched and never projected (décision 12)
    public void Google_ProjectsNoPhotoAndTheCardsOwnPartialBirthday()
    {
        var p = VCardProjector.Project(Card("google.vcf", 0));

        Assert.Null(p.Photo);
        Assert.Equal("--03-15", p.Birthday);                 // the 3.0 extended form, stored as written
        Assert.Equal("item1", p.Addresses[2].Line.GroupName);
        Assert.All(p.Addresses, a => Assert.Equal(101, a.Line.Pref));
        Assert.Equal(3, p.Addresses.Count);
    }

    [Fact] // Thunderbird 4.0: PREF is a parameter, and TYPE may be a quoted comma list
    public void Thunderbird_ProjectsPrefParameterAndQuotedTypeList()
    {
        var p = VCardProjector.Project(Card("thunderbird.vcf", 0));

        Assert.Equal(1, p.Addresses[0].Line.Pref);
        Assert.Equal(101, p.Addresses[1].Line.Pref);
        Assert.Equal("voice,work", p.Phones[0].Line.Type);   // quotes stripped, the list kept whole
        Assert.Equal(1, p.Phones[1].Line.Pref);
        Assert.Equal("--0315", p.Birthday);                  // the 4.0 basic form
        Assert.Equal("4c1f6b8e-0d2a-4a5f-9c31-6b7d8e9f0a1b", p.Uid);
    }

    [Fact] // DAVx⁵ 4.0: RFC 9554 components on the ADR, and a data: photo
    public void Davx5_IgnoresTheDuplicateStreetAndProjectsTheDataPhoto()
    {
        var p = VCardProjector.Project(Card("davx5.vcf", 0));

        // Décision 4's RFC 9554 corollary: STREETNAME/STREETNUMBER are present, so the street
        // component is their combined duplicate and the projection drops it rather than show it twice.
        Assert.Null(p.PostalAddresses[0].Street);
        Assert.Equal("Charleroi", p.PostalAddresses[0].Locality);
        Assert.Equal("image/png", p.Photo?.MediaType);
        Assert.Equal(1, p.Addresses[0].Line.Pref);
        Assert.Equal("TYPE=work;PID=2.1", p.Addresses[1].Line.Params); // PID reaches the column verbatim

        // Observed on FolkerKinzel 8.2.0, not a requirement of the spec: the card writes
        // UID:urn:uuid:6f4b… and the library hands back the bare UUID, the URN scheme stripped.
        // Reported as a design question for 4c — CardDAV clients compare UIDs (task 9 report).
        Assert.Equal("6f4b2c1a-8d3e-4f5a-b1c2-d3e4f5a6b7c8", p.Uid);
    }

    // ---- survival --------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllClients))]
    public void Corpus_SurvivesASingleFieldEdit(string file)
    {
        var card = Card(file, 0);
        var p = VCardProjector.Project(card);
        var write = WriteFrom(p) with { Notes = "edited" }; // one field moves, nothing else
        var output = VCardComposer.Compose(card, p.Uid ?? Uid, write);
        var reparsed = VCardProjector.Project(output);

        Assert.Equal("edited", reparsed.Notes);
        // Every property no entry point models is still there, occurrence for occurrence.
        Assert.Equal(NonStandardNames(card), NonStandardNames(output));
        // Every grouped label still designates its own property.
        Assert.Equal(GroupsOf(card, "X-ABLabel"), GroupsOf(output, "X-ABLabel"));
        AssertLinesSurvive(p.Addresses.Select(a => a.Line), reparsed.Addresses.Select(a => a.Line));
        AssertLinesSurvive(p.Phones.Select(t => t.Line), reparsed.Phones.Select(t => t.Line));
        AssertLinesSurvive(
            p.PostalAddresses.Select(a => a.Line), reparsed.PostalAddresses.Select(a => a.Line));
    }

    [Fact] // décision 4: the components past RFC 6350's seven are never read, so never rewritten
    public void Davx5_KeepsTheRfc9554AddressComponentsAcrossAnEdit()
    {
        var card = Card("davx5.vcf", 0);
        var p = VCardProjector.Project(card);
        var output = VCardComposer.Compose(card, p.Uid!, WriteFrom(p) with { Notes = "edited" });

        var adr = Line(output, "ADR");
        Assert.Contains(";3;45;Rue du Modele;", adr);        // floor, street number, street name
        Assert.Contains("Face a l'eglise", adr);             // landmark
        Assert.Contains("LABEL=", adr);                      // and the parameter that is not TYPE

        // The writer re-derives RFC 9554's backward-compatible street and extended components from
        // them, so those two bytes do change — and the reader ignores them again for the same
        // reason, which is why the fiche the user sees is byte-stable across the edit.
        var reparsed = VCardProjector.Project(output).PostalAddresses[0];
        Assert.Equal(p.PostalAddresses[0] with { Line = reparsed.Line }, reparsed);
    }

    [Fact] // the second iPhone card: the X-AB* families FolkerKinzel does not model at all
    public void Iphone_KeepsTheUnmodelledAppleFamiliesOfACompanyCard()
    {
        var card = Card("iphone.vcf", 1);
        var p = VCardProjector.Project(card);
        var output = VCardComposer.Compose(card, Uid, WriteFrom(p) with { Notes = "edited" });

        Assert.Equal(NonStandardNames(card), NonStandardNames(output));
        Assert.Contains("X-ABShowAs:COMPANY", Lines(output));
        Assert.Contains("item3.X-ABDATE;type=pref:2019-09-01", Lines(output));
        Assert.Contains("item4.X-ABRELATEDNAMES;type=pref:Prenom Test", Lines(output));
        Assert.Equal(GroupsOf(card, "X-ABADR"), GroupsOf(output, "X-ABADR"));
        Assert.Equal(GroupsOf(card, "X-ABLabel"), GroupsOf(output, "X-ABLabel"));
    }

    [Fact] // the N components of a 4.0 card, middle name included, survive the same edit
    public void Davx5_KeepsTheNameComponentsAcrossAnEdit()
    {
        var card = Card("davx5.vcf", 0);
        var p = VCardProjector.Project(card);
        var output = VCardComposer.Compose(card, p.Uid!, WriteFrom(p) with { Notes = "edited" });

        var n = Line(output, "N");
        Assert.Equal("Modele;Sophie;Anne;;", n[(n.IndexOf(':') + 1)..]);
    }

    // ---- library behaviour, observed on the corpus rather than assumed ----------------------------

    [Fact]
    public void Corpus_PinsLibraryBehaviour()
    {
        var iphone = Card("iphone.vcf", 0);
        var p = VCardProjector.Project(iphone);
        var output = VCardComposer.Compose(iphone, p.Uid ?? Uid, WriteFrom(p) with { Notes = "edited" });

        // 1. X-ABLabel is the one X-AB* FolkerKinzel models (décision 6), so it could have been
        //    moved out of its group. Observed on 8.2.0: it keeps it. Read what this test does and
        //    does not prove — X-ABLABEL is absent from VCardComposer.OwnedNames, so in the shipped
        //    configuration SpliceUnmodelledFamilies rewrites these lines from the input anyway and
        //    the green below is overdetermined: it says the group survives, not who saved it. That
        //    the library's 3.0 writer alone re-emits "item1.X-ABLabel:_$!<Home>!$_" was established
        //    by probing FolkerKinzel 8.2.0 outside this suite (task 9 report, constat A). A
        //    constat either way, not a promise: this is what speaks if a later version moves it.
        Assert.Equal(["item1", "item2", "item4"], GroupsOf(output, "X-ABLabel"));
        Assert.Contains("item1.X-ABLabel:_$!<Home>!$_", Lines(output));

        // 2. A partial BDAY re-emitted in 3.0. Décision 11 says the column's own spelling wins
        //    whatever the library would make of the date; EnforceBirthday is what enforces it.
        var google = Card("google.vcf", 0);
        var gp = VCardProjector.Project(google);
        var partial = VCardComposer.Compose(google, gp.Uid ?? Uid, WriteFrom(gp) with { Notes = "e" });
        Assert.Equal("BDAY:--03-15", Line(partial, "BDAY"));
        Assert.Equal("--03-15", VCardProjector.Project(partial).Birthday);

        // And the Apple form, whose X- parameter the 3.0 writer drops unless the repair restores it.
        Assert.Equal("BDAY;X-APPLE-OMIT-YEAR=1604:1604-03-15", Line(output, "BDAY"));
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static string Fixture(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "VCards", file));

    private static string Card(string file, int index) => VCardSplitter.Split(Fixture(file))[index].Text;

    private static ContactWrite WriteFrom(ContactProjection p) => new(
        p.FirstName, p.LastName, p.Nickname, p.DisplayName, p.MiddleName, p.NamePrefix, p.NameSuffix,
        p.Organization, p.Department, p.JobTitle, p.Birthday, p.Website, p.Notes, false,
        [.. p.Addresses.Select(e => new ContactWriteEmail(e.Line.Position, e.Address, e.Line.Type))],
        [.. p.Phones.Select(t => new ContactWritePhone(t.Line.Position, t.Number, t.Line.Type))],
        [.. p.PostalAddresses.Select(a => new ContactWriteAddress(
            a.Line.Position, a.Line.Type, a.PoBox, a.Extended, a.Street, a.Locality, a.Region,
            a.PostalCode, a.Country))],
        "manual");

    /// <summary>
    /// The survival claim, line by line: same group, same normalised <c>PREF</c>, and exactly the
    /// parameters the card carried still on its own property. Not a byte comparison of the block —
    /// décision 4 lets the editor replace <c>TYPE</c>, the 3.0 writer respells it and adds its own
    /// <c>INTERNET</c>, and the round trip is not promised byte for byte (décision 6). Everything
    /// that is not <c>TYPE</c> is compared both ways: the writer adds no such parameter of its own
    /// on EMAIL/TEL/ADR, so an <b>appearing</b> one is a regression too — a re-enabled
    /// <c>SetPropertyIDs</c> stamping a <c>PID</c> on a line that carried none is the case.
    /// </summary>
    private static void AssertLinesSurvive(
        IEnumerable<ProjectedLine> before, IEnumerable<ProjectedLine> after)
    {
        var input = before.ToList();
        var output = after.ToList();
        Assert.Equal(input.Count, output.Count);
        for (var i = 0; i < input.Count; i++)
        {
            Assert.Equal(input[i].GroupName, output[i].GroupName);
            Assert.Equal(input[i].Pref, output[i].Pref);
            Assert.Equal(OtherThanType(input[i].Params), OtherThanType(output[i].Params));
            foreach (var token in TypeTokens(input[i].Params))
                Assert.Contains(token, TypeTokens(output[i].Params));
        }
    }

    private static List<(string Key, string Value)> OtherThanType(string block) =>
        [.. Parameters(block)
            .Where(p => !p.Key.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Key, StringComparer.Ordinal)];

    // PREF is carried by the pref column and re-emitted from it; INTERNET is the 3.0 writer's own.
    private static HashSet<string> TypeTokens(string block) =>
        new(Parameters(block)
                .Where(p => p.Key.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Value.Split(','))
                .Select(t => t.Trim())
                .Where(t => t.Length > 0 && !t.Equals("PREF", StringComparison.OrdinalIgnoreCase)
                    && !t.Equals("INTERNET", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

    // Values are unquoted before comparison: DQUOTEs are RFC 6350 syntax, required only around a
    // value carrying ',', ';' or ':', and the writer re-emits one that needs none without them. The
    // ';' split honours those quotes, as VCardComposer.SplitOutsideQuotes does — a real export's
    // LABEL="…;…" would otherwise be torn in two, silently and in the reader's favour.
    private static List<(string Key, string Value)> Parameters(string block) =>
        [.. SplitOutsideQuotes(block)
            .Where(p => p.Length > 0)
            .Select(p => p.IndexOf('=') is var eq && eq < 0
                ? (p.Trim().ToUpperInvariant(), string.Empty)
                : (p[..eq].Trim().ToUpperInvariant(), p[(eq + 1)..].Trim('"')))];

    private static List<string> SplitOutsideQuotes(string block)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i < block.Length; i++)
        {
            if (block[i] == '"') inQuotes = !inQuotes;
            else if (block[i] == ';' && !inQuotes)
            {
                parts.Add(block[start..i]);
                start = i + 1;
            }
        }

        parts.Add(block[start..]);
        return parts;
    }

    /// <summary>Every X- property name the card carries, in order — the unmodelled families.</summary>
    private static List<string> NonStandardNames(string card) =>
        [.. Lines(card).Select(VCardComposer.NameOf)
            .Where(n => n.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.ToUpperInvariant())
            .Order(StringComparer.Ordinal)];

    /// <summary>The group prefix of every occurrence of one property, in card order, "" when none.</summary>
    private static List<string> GroupsOf(string card, string name) =>
        [.. Lines(card)
            .Where(l => VCardComposer.NameOf(l).Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.IndexOf('.') is var dot && dot >= 0 && dot < l.IndexOfAny([';', ':'])
                ? l[..dot] : string.Empty)];

    private static string Line(string card, string name) =>
        Lines(card).First(l => VCardComposer.NameOf(l).Equals(name, StringComparison.OrdinalIgnoreCase));

    // Unfolded logical lines, whatever the host's newline: a fixture is CRLF, output is CRLF, and
    // neither is asserted on.
    private static List<string> Lines(string card) =>
        [.. card.Replace("\r\n", "\n").Replace("\n ", string.Empty).Replace("\n\t", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)];
}
