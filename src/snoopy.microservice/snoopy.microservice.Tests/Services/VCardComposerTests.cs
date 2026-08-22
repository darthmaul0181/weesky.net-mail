using FolkerKinzel.VCards.Enums;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class VCardComposerTests
{
    private const string Uid = "u-42";

    private static readonly ContactWrite MinimalWrite = WriteWith();

    private static readonly string CardWithoutUid = Card("NOTE:n");

    // The exact shape ContactVCardWriter produced before this slice: no UID, no REV.
    private const string LegacyWriterCard = "BEGIN:VCARD\r\nVERSION:3.0\r\n" +
        "N:Ancien;Jean;;;\r\nFN:Jean Ancien\r\nEMAIL;TYPE=INTERNET:old@n.be\r\n" +
        "TEL;TYPE=HOME,VOICE:+3221234567\r\nORG:Acme;Ventes\r\n" +
        "ADR;TYPE=HOME:;;Rue Basse 2;Namur;;5000;Belgique\r\nNOTE:Client fidele\r\n" +
        "BDAY:1985-04-12\r\nEND:VCARD\r\n";

    private static string Card(params string[] lines) =>
        "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:X\r\n"
        + string.Concat(lines.Select(l => l + "\r\n")) + "END:VCARD\r\n";

    private static ContactWrite WriteWith(
        string? firstName = null,
        string? lastName = null,
        string? nickname = null,
        string? displayName = null,
        string? middleName = null,
        string? namePrefix = null,
        string? birthday = null,
        string? website = null,
        string? organization = null,
        string? department = null,
        string? jobTitle = null,
        string? notes = null,
        IReadOnlyList<ContactWriteEmail>? addresses = null,
        IReadOnlyList<ContactWritePhone>? phones = null,
        IReadOnlyList<ContactWriteAddress>? postalAddresses = null) =>
        new(firstName, lastName, nickname, displayName, middleName, namePrefix, null, organization,
            department, jobTitle, birthday, website, notes, false,
            addresses ?? [], phones ?? [], postalAddresses ?? [], "manual");

    // The unfolded logical line carrying the given fragment.
    private static string LineWith(string output, string fragment) =>
        output.Replace("\r\n ", "").Replace("\r\n\t", "").Split("\r\n")
            .Single(l => l.Contains(fragment));

    // Name + parameter block of the first TEL, group prefix included, unfolded.
    private static string ParamsOfFirstTel(string output)
    {
        var unfolded = output.Replace("\r\n ", "").Replace("\r\n\t", "");
        var line = unfolded.Split("\r\n").First(l =>
            l.StartsWith("TEL", StringComparison.OrdinalIgnoreCase)
            || l.Split(':')[0].Split(';')[0].EndsWith(".TEL", StringComparison.OrdinalIgnoreCase));
        return line[..line.IndexOf(':')];
    }

    [Fact] // les deux drapeaux, et SetPropertyIDs dehors — le pin de la décision 6
    public void Options_PinTheNonNegotiableFlags()
    {
        Assert.True(VCardComposer.SerializationOptions.HasFlag(VcfOpts.WriteNonStandardProperties));
        Assert.True(VCardComposer.SerializationOptions.HasFlag(VcfOpts.WriteNonStandardParameters));
        Assert.False(VCardComposer.SerializationOptions.HasFlag(VcfOpts.SetPropertyIDs));
    }

    [Fact] // décision 4 : la valeur change, groupe + params + X- restent
    public void Compose_ReplacesAValueInPlace()
    {
        var card = Card("item1.TEL;TYPE=WORK;X-FOO=bar:+3221111111");
        var write = WriteWith(phones: [new ContactWritePhone(0, "+3229999999", "WORK")]);
        var output = VCardComposer.Compose(card, Uid, write);
        Assert.Contains("item1.TEL", output);
        Assert.Contains("X-FOO=bar", output);
        Assert.Contains("+3229999999", output);
        Assert.DoesNotContain("+3221111111", output);
    }

    [Fact] // type changé sur ligne existante : seul TYPE bouge, le jeton PREF du 3.0 survit
    public void Compose_ReplacesOnlyTheTypeParameterKeepingPref()
    {
        var card = Card("TEL;TYPE=HOME,PREF;X-A=1:+321");
        var output = VCardComposer.Compose(card, Uid, WriteWith(phones: [new(0, "+321", "WORK")]));
        Assert.Contains("PREF", ParamsOfFirstTel(output));
        Assert.Contains("WORK", ParamsOfFirstTel(output));
        Assert.DoesNotContain("HOME", ParamsOfFirstTel(output));
        Assert.Contains("X-A=1", ParamsOfFirstTel(output));
    }

    [Fact] // le jeton PREF vient de Preference, jamais du champ type que la projection lui renvoie
    public void Compose_DoesNotDuplicateThePrefTokenTheProjectionEchoesBack()
    {
        var card = Card("TEL;TYPE=HOME,PREF:+321");
        var output = VCardComposer.Compose(card, Uid, WriteWith(phones: [new(0, "+321", "HOME,PREF")]));
        var parameters = ParamsOfFirstTel(output);
        Assert.Contains("HOME", parameters);
        Assert.Equal(2, parameters.Split("PREF").Length); // un seul jeton PREF
    }

    [Fact] // position absente de la fiche = propriété supprimée ; position hors carte = ligne ajoutée en fin
    public void Compose_DeletesRemovedAndAppendsUnmatched()
    {
        var card = Card("TEL:+321", "TEL:+322");
        var write = WriteWith(phones:
            [new ContactWritePhone(0, "+321", ""), new ContactWritePhone(null, "+329", "CELL")]);
        var output = VCardComposer.Compose(card, Uid, write);
        Assert.Contains("+321", output);
        Assert.DoesNotContain("+322", output);
        Assert.Contains("TEL;TYPE=CELL:+329", output);
    }

    [Fact] // ADR : 7 premières composantes remplacées, les composantes RFC 9554 (8..18) intactes
    public void Compose_LeavesExtendedAdrComponentsAlone()
    {
        var card = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:X\r\n" +
            "ADR;TYPE=HOME:PO;;Old Street;Bxl;;1000;BE;Room 5;Apt 2;Floor 3;42;Main\r\n" +
            "END:VCARD\r\n";
        var write = WriteWith(postalAddresses:
            [new ContactWriteAddress(0, "HOME", "PO", null, "New Street", "Bxl", null, "1000", "BE")]);
        var output = VCardComposer.Compose(card, Uid, write);
        Assert.Contains("New Street", output);
        Assert.Contains("Room 5", output);
        Assert.Contains("Floor 3", output);
        Assert.DoesNotContain("Old Street", output);
    }

    [Theory] // versions : 3.0 reste 3.0, 4.0 reste 4.0, 2.1 promu 3.0 (décision 7)
    [InlineData("3.0", "3.0")]
    [InlineData("4.0", "4.0")]
    [InlineData("2.1", "3.0")]
    public void Compose_EmitsInTheCardsVersion(string input, string expected)
    {
        var card = $"BEGIN:VCARD\r\nVERSION:{input}\r\nFN:X\r\nEND:VCARD\r\n";
        var output = VCardComposer.Compose(card, Uid, MinimalWrite);
        Assert.Contains($"VERSION:{expected}", output);
    }

    [Fact] // invariants : UID = colonne (ajouté s'il manque), REV rafraîchi
    public void Compose_EnforcesUidAndRev()
    {
        var output = VCardComposer.Compose(CardWithoutUid, "the-uid", MinimalWrite);
        Assert.Contains("UID:the-uid", output);
        Assert.Contains("REV:", output);
    }

    [Fact] // ComposeNew : VERSION:3.0, FN par la chaîne de repli, toute fiche a une carte (même nom seul)
    public void ComposeNew_AlwaysProducesACard()
    {
        var output = VCardComposer.ComposeNew("u1", WriteWith(firstName: "Ana")); // rien d'autre
        Assert.Contains("VERSION:3.0", output);
        Assert.Contains("FN:Ana", output);
        Assert.Contains("UID:u1", output);
    }

    // Chaque famille de propriétés sur une carte neuve — la couverture de valeur qu'apportait
    // ContactVCardWriter, que le composeur absorbe (spec, § Le moteur).
    [Fact]
    public void ComposeNew_EmitsEveryFamilyOfANewCard()
    {
        var output = VCardComposer.ComposeNew("u1", WriteWith(
            firstName: "Bruno", lastName: "Mertens", nickname: "bruno", middleName: "J",
            namePrefix: "Mr", birthday: "1980-01-15", website: "https://x.be",
            organization: "Weesky", department: "Support", jobTitle: "Engineer", notes: "a note",
            addresses: [new ContactWriteEmail(null, "bruno@example.com", "INTERNET")],
            phones:
            [
                new ContactWritePhone(null, "+32470000000", "CELL"),
                new ContactWritePhone(null, "+3281000000", "HOME,VOICE"),
                new ContactWritePhone(null, "+3281000001", "WORK,FAX"),
            ],
            postalAddresses:
            [
                new ContactWriteAddress(null, "HOME", null, null, "Rue X 1", "Namur", null, "5000", "Belgium"),
                new ContactWriteAddress(null, "WORK", null, "Room 3", "Rue Y 2", null, null, null, null),
            ]));

        Assert.Contains("N:Mertens;Bruno;J;Mr;", output);
        Assert.Contains("FN:Bruno J Mertens", output);
        Assert.Contains("NICKNAME:bruno", output);
        Assert.Contains("EMAIL;TYPE=INTERNET:bruno@example.com", output);
        Assert.Contains("TEL;TYPE=CELL:+32470000000", output);
        Assert.Contains("TEL;TYPE=HOME,VOICE:+3281000000", output);
        Assert.Contains("TEL;TYPE=WORK,FAX:+3281000001", output);
        Assert.Contains("ORG:Weesky;Support", output);
        Assert.Contains("TITLE:Engineer", output);
        Assert.Contains("NOTE:a note", output);
        Assert.Contains("BDAY:1980-01-15", output);
        Assert.Contains("URL:https://x.be", output);
        Assert.Contains("ADR;TYPE=HOME:;;Rue X 1;Namur;;5000;Belgium", output);
        Assert.Contains("ADR;TYPE=WORK:;Room 3;Rue Y 2;;;;", output);
    }

    [Fact] // les séparateurs vCard sont échappés dans la valeur, pas laissés couper la propriété
    public void ComposeNew_EscapesTheSeparators()
    {
        var output = VCardComposer.ComposeNew("u1", WriteWith(firstName: "Ana", notes: "a; b, c\ne"));

        Assert.Equal(@"NOTE:a\; b\, c\ne", LineWith(output, "NOTE"));
    }

    [Fact] // la chaîne de repli continue au-delà du nom : pseudo, puis première adresse
    public void ComposeNew_FallsBackToTheNicknameForFn()
    {
        var output = VCardComposer.ComposeNew("u1", WriteWith(nickname: "Ana"));
        Assert.Contains("FN:Ana", output);
    }

    // FolkerKinzel 8.2.0 remplit le N obligatoire de la 3.0 d'un « ? » quand notre nom est vide.
    // La forme vide conforme à RFC 2426 est N:;;;;, celle qu'émettent les autres clients.
    [Fact]
    public void ComposeNew_NamelessCard_WritesTheEmptyNRatherThanThePlaceholder()
    {
        var output = VCardComposer.ComposeNew("u1", WriteWith(
            nickname: "Marie-Rose Molhan",
            addresses: [new ContactWriteEmail(null, "marie-rose.molhan@weesky.be", string.Empty)]));

        Assert.Contains("N:;;;;", output);
        Assert.DoesNotContain('?', output);
    }

    // FN est obligatoire dans les deux versions, et l'écrivain le remplit du même « ? » : sans nom,
    // sans pseudo et sans adresse la chaîne de repli rend "", et display_name gagnait le remplissage.
    [Theory]
    [InlineData("3.0")]
    [InlineData("4.0")]
    public void Compose_CardWithNothingToNameIt_WritesAnEmptyFn(string version)
    {
        var card = $"BEGIN:VCARD\r\nVERSION:{version}\r\nFN:X\r\nEND:VCARD\r\n";

        var output = VCardComposer.Compose(card, Uid, MinimalWrite);

        Assert.Contains("FN:\r\n", output);
        Assert.DoesNotContain('?', output);
    }

    [Fact] // le remplissage du N est un fait de la 3.0 : la 4.0 n'écrit aucune ligne N
    public void Compose_NamelessCardIn40_WritesNoNLine()
    {
        var card = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:X\r\nEND:VCARD\r\n";

        var output = VCardComposer.Compose(card, Uid, MinimalWrite);

        Assert.DoesNotContain("\r\nN:", output);
        Assert.DoesNotContain('?', output);
    }

    // La réparation ne vise que notre propre vide : un « ? » que l'utilisateur a tapé comme nom de
    // famille est une valeur, pas un remplissage, et le composeur ne la lui reprend pas.
    [Fact]
    public void Compose_QuestionMarkTypedAsASurname_IsKept()
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:?\r\nN:?;;;;\r\nEND:VCARD\r\n";

        var output = VCardComposer.Compose(card, Uid, WriteWith(lastName: "?", displayName: "?"));

        Assert.Contains("N:?;;;;", output);
        Assert.Contains("FN:?", output);
    }

    // Le vrai sort d'une carte étrangère portant N:?;;;; quand elle passe par l'éditeur : Apply
    // écrase le nom du modèle par celui des colonnes — que le projecteur rend désormais vides —
    // donc ce que la réparation efface est le remplissage de l'écrivain, jamais les octets reçus.
    // Les chemins verbatim (import .vcf, PUT de 4c) n'appellent pas Emit et ne voient rien de ceci.
    [Fact]
    public void Compose_ForeignPlaceholderCard_EmitsTheColumnsEmptyName()
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:?\r\nN:?;;;;\r\nEND:VCARD\r\n";

        var output = VCardComposer.Compose(card, Uid, MinimalWrite);

        Assert.Contains("N:;;;;", output);
        Assert.DoesNotContain('?', output);
    }

    // Le second verrou — la valeur émise ne doit être faite que de « ? » et de « ; » — porte sur un
    // chemin réel : avec un N nommé et aucun FN, l'écrivain 8.2.0 synthétise FN depuis le N, et
    // MergeFill calcule son repli sur les noms de l'import, pas sur ceux de la carte. Un merge vide
    // laisse donc DisplayNames nul, Nameless vrai — sans ce verrou, Blank effacerait un vrai nom.
    [Fact]
    public void MergeFill_EmptyWriteOnANamedCardWithoutFn_KeepsTheSynthesisedFn()
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Smith;John;;;\r\nEND:VCARD\r\n";

        var output = VCardComposer.MergeFill(card, "u1", new MergeWrite(null, null, null, []));

        Assert.Contains("FN:John Smith", output);
        Assert.Contains("N:Smith;John;;;", output);
    }

    // Le jumeau de Backfill_ReconcilesWithoutDestroyingTheCard côté import : un CSV Outlook qui
    // porte un Middle Name sans Display Name doit garder le FN de 3d, prénom + milieu + nom.
    [Fact]
    public void ComposeNew_KeepsTheMiddleNameInTheFallbackFn()
    {
        var output = VCardComposer.ComposeNew(
            "u1", WriteWith(firstName: "Jean", lastName: "Dupont", middleName: "Pierre"));

        Assert.Contains("FN:Jean Pierre Dupont", output);
    }

    [Fact] // Reconcile : borné — les TEL/ADR/ORG/BDAY/NOTE du brut survivent à des colonnes vides
    public void Reconcile_NeverTouchesWhatOnlyTheCardCarries()
    {
        var output = VCardComposer.Reconcile(
            LegacyWriterCard, "u1", new ReconcileWrite("Jean", "Nouveau", null, ["j@n.be"]));
        Assert.Contains("TEL", output);
        Assert.Contains("ORG", output);
        Assert.Contains("BDAY", output);
        Assert.Contains("UID:u1", output);
        Assert.Contains("N:Nouveau;Jean", output);
        Assert.Contains("ADR", output);
        Assert.Contains("NOTE:Client fidele", output);
        Assert.Contains("EMAIL;TYPE=INTERNET:j@n.be", output); // remplacée en place, TYPE conservé
        Assert.DoesNotContain("old@n.be", output);
    }

    [Fact] // MergeFill : FN existant jamais écrasé, adresses ajoutées en fin
    public void MergeFill_KeepsAnExistingFn()
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Dr. X\r\nEND:VCARD\r\n";
        var output = VCardComposer.MergeFill(card, "u1", new MergeWrite("Y", null, null, []));
        Assert.Contains("FN:Dr. X", output);
        Assert.DoesNotContain("FN:Y", output);
        Assert.Contains("N:;Y", output); // le prénom non nul est posé, lui
    }

    [Fact] // MergeFill : les adresses de l'import s'ajoutent, celles de la carte restent
    public void MergeFill_AppendsTheImportsAddresses()
    {
        var card = Card("EMAIL;TYPE=INTERNET:a@b.c");
        var output = VCardComposer.MergeFill(card, "u1", new MergeWrite(null, null, null, ["new@x.y"]));
        Assert.Contains("a@b.c", output);
        Assert.Contains("new@x.y", output);
    }

    // ORG porte deux champs sur une seule ligne : recomposer la ligne à partir de la seule moitié
    // nommée effacerait l'autre, alors que rien dans l'écriture ne l'a demandé.
    [Theory]
    [InlineData("NewCorp", null, "ORG:NewCorp;Ventes")]
    [InlineData(null, "Support", "ORG:Acme;Support")]
    [InlineData("", null, "ORG:;Ventes")]
    [InlineData(null, "", "ORG:Acme")]
    public void Compose_NamingOneHalfOfTheOrganization_KeepsTheOther(
        string? organization, string? department, string expected)
    {
        var output = VCardComposer.Compose(Card("ORG:Acme;Ventes"), Uid,
            WriteWith(organization: organization, department: department));

        Assert.Equal(expected, LineWith(output, "ORG"));
    }

    [Fact] // les deux moitiés vides passent par la suppression : la ligne part, les suivantes restent
    public void Compose_ClearingBothHalvesOfTheOrganization_DropsOnlyTheFirstOccurrence()
    {
        var output = VCardComposer.Compose(Card("ORG:Acme;Ventes", "ORG:Umbrella"), Uid,
            WriteWith(organization: string.Empty, department: string.Empty));

        Assert.Equal("ORG:Umbrella", LineWith(output, "ORG"));
    }

    [Fact] // la 2e occurrence de NOTE/TITLE/ORG/NICKNAME d'une carte 3.0 survit, octet pour octet
    public void Compose_KeepsOccurrenceTwoOfEveryCollapsedScalar()
    {
        var card = Card("NOTE:premiere note", "NOTE:seconde note", "TITLE:Vendeur", "TITLE:Consul",
            "ORG:Acme", "ORG:Umbrella", "NICKNAME:Jo", "NICKNAME:Bob");
        var write = WriteWith(notes: "note editee", jobTitle: "Chef", organization: "NewCorp",
            nickname: "Zed");
        var output = VCardComposer.Compose(card, Uid, write);
        Assert.Contains("NOTE:note editee", output);
        Assert.Contains("NOTE:seconde note", output);
        Assert.DoesNotContain("premiere note", output);
        Assert.Contains("TITLE:Chef", output);
        Assert.Contains("TITLE:Consul", output);
        Assert.Contains("ORG:NewCorp", output);
        Assert.Contains("ORG:Umbrella", output);
        Assert.Contains("NICKNAME:Zed", output);
        Assert.Contains("NICKNAME:Bob", output);
    }

    [Fact] // le rattrapage surtout : une carte legacy portant deux NOTE les garde toutes les deux
    public void Reconcile_KeepsEveryNoteOfALegacyCard()
    {
        var card = Card("NOTE:premiere", "NOTE:seconde", "TEL:+321");
        var output = VCardComposer.Reconcile(card, "u1", new ReconcileWrite("A", "B", null, []));
        Assert.Contains("NOTE:premiere", output);
        Assert.Contains("NOTE:seconde", output);
    }

    [Fact] // une famille non modélisée traverse l'édition octet pour octet, ses X- compris
    public void Compose_KeepsAnUnmodelledPropertyVerbatim()
    {
        var card = Card("KEY;TYPE=PGP;X-K=1:abc123");
        var output = VCardComposer.Compose(card, Uid, WriteWith(firstName: "Ana"));
        Assert.Contains("KEY;TYPE=PGP;X-K=1:abc123", output);
    }

    [Fact] // PREF sur une occurrence tardive : l'édition survit ET l'occurrence préférée aussi
    public void Compose_KeepsTheEditWhenALaterOccurrenceCarriesPref()
    {
        var card = Card(
            "URL:http://one.example", "URL;TYPE=PREF:http://two.example",
            "NOTE:ancienne", "NOTE;TYPE=PREF:seconde",
            "TITLE:Vendeur", "TITLE;TYPE=PREF:Consul",
            "ORG:Acme", "ORG;TYPE=PREF:Umbrella",
            "NICKNAME:Jim", "NICKNAME;TYPE=PREF:Bob");
        var write = WriteWith(nickname: "Zed", website: "http://new.example",
            organization: "NewCorp", jobTitle: "Chef", notes: "note editee");
        var output = VCardComposer.Compose(card, Uid, write);
        Assert.Contains("URL:http://new.example", output);
        Assert.Contains("URL;TYPE=PREF:http://two.example", output);
        Assert.Contains("NOTE:note editee", output);
        Assert.Contains("NOTE;TYPE=PREF:seconde", output);
        Assert.Contains("TITLE:Chef", output);
        Assert.Contains("TITLE;TYPE=PREF:Consul", output);
        Assert.Contains("ORG:NewCorp", output);
        Assert.Contains("ORG;TYPE=PREF:Umbrella", output);
        Assert.Contains("NICKNAME:Zed", output);
        Assert.Contains("NICKNAME;TYPE=PREF:Bob", output);
        Assert.DoesNotContain("http://one.example", output);
        Assert.DoesNotContain("NOTE:ancienne", output);
        Assert.DoesNotContain("TITLE:Vendeur", output);
        Assert.DoesNotContain("ORG:Acme", output);
        Assert.DoesNotContain("NICKNAME:Jim", output);
    }

    [Fact] // un \r nu dans une ligne non modélisée ne réinjecte jamais une frontière de carte
    public void Compose_NeutralisesALoneCarriageReturnInAnUnmodelledLine()
    {
        var card = Card(
            "X-EVIL:a\rEND:VCARD\rBEGIN:VCARD\rVERSION:3.0\rFN:Injected\rX-SMUGGLED:b");
        var output = VCardComposer.Compose(card, Uid, WriteWith(firstName: "Ana"));
        Assert.Equal(2, output.Split("BEGIN:VCARD").Length); // une seule carte
        Assert.DoesNotContain("FN:Injected", output);
        Assert.DoesNotContain("X-SMUGGLED", output); // la 2e carte du brut n'est pas fusionnée
        Assert.Contains("X-EVIL:a", output);
    }

    [Fact] // même règle un cran plus bas : une liste d'adresses vide ne touche pas au bloc EMAIL
    public void Reconcile_LeavesTheCardsEmailBlockWhenTheColumnsHaveNone()
    {
        var card = Card("EMAIL;TYPE=INTERNET:only@card.be");
        var output = VCardComposer.Reconcile(card, "u1", new ReconcileWrite("Jean", "Doe", null, []));
        Assert.Contains("only@card.be", output);
    }

    [Fact] // Reconcile est une réconciliation : une colonne vide ne supprime jamais (ruling revue 2)
    public void Reconcile_LeavesTheCardsNicknameWhenTheColumnIsEmpty()
    {
        var card = Card("NICKNAME:Johnny");
        var output = VCardComposer.Reconcile(card, "u1", new ReconcileWrite("Jean", "Doe", null, []));
        Assert.Contains("NICKNAME:Johnny", output);
    }

    [Fact] // le réparateur suit l'ordre du sérialiseur (tri par PREF) — jamais de X- croisés
    public void Compose_RepairsEachTelAgainstTheWritersOrder()
    {
        var card = Card("TEL;TYPE=HOME;X-A=1:+321", "TEL;TYPE=WORK,PREF;X-B=2:+322");
        var write = WriteWith(phones:
            [new ContactWritePhone(0, "+321", "HOME"), new ContactWritePhone(1, "+322", "WORK,PREF")]);
        var output = VCardComposer.Compose(card, Uid, write);
        var first = LineWith(output, "+321");
        var second = LineWith(output, "+322");
        Assert.Contains("X-A=1", first);
        Assert.DoesNotContain("X-B=2", first);
        Assert.Contains("X-B=2", second);
        Assert.DoesNotContain("X-A=1", second);
    }

    [Fact] // survie : BDAY texte ré-émis tel quel même en 3.0 (décision 11) — épingle la bibliothèque
    public void Compose_EmitsAPartialBdayVerbatim()
    {
        var output = VCardComposer.Compose(Card(), Uid, WriteWith(birthday: "--0315"));
        Assert.Contains("BDAY:--0315", output);
        Assert.DoesNotContain("0004", output); // la bibliothèque inventait l'année 4
    }

    [Fact] // une ligne postale sans aucune composante n'écrit pas d'ADR vide dans la carte
    public void Compose_DropsAValuelessPostalLine()
    {
        var write = WriteWith(postalAddresses:
            [new ContactWriteAddress(null, "HOME", null, null, null, null, null, null, null)]);
        var output = VCardComposer.Compose(Card(), Uid, write);
        Assert.DoesNotContain("ADR", output);
    }

    [Fact] // décision 5 : website remplace la première URL, la seconde survit — même en 3.0
    public void Compose_KeepsTheSecondUrlOfA30Card()
    {
        var card = Card("URL:http://one.example", "URL:http://two.example");
        var output = VCardComposer.Compose(card, Uid, WriteWith(website: "http://new.example"));
        Assert.Contains("http://new.example", output);
        Assert.Contains("http://two.example", output);
        Assert.DoesNotContain("http://one.example", output);
    }

    [Fact] // décision 12 : le composeur ne traite pas PHOTO — elle survit, repliement compris
    public void Compose_KeepsAPhotoItDoesNotModel()
    {
        var base64 = Convert.ToBase64String(Enumerable.Range(0, 120).Select(i => (byte)i).ToArray());
        var card = Card($"PHOTO;ENCODING=b;TYPE=JPEG:{base64}");
        var output = VCardComposer.Compose(card, Uid, WriteWith(firstName: "Ana"));
        var unfolded = output.Replace("\r\n ", "").Replace("\r\n\t", "");
        Assert.Contains(base64, unfolded);
    }

    [Fact] // les propriétés X- et leur groupe survivent à toute édition (décision 6)
    public void Compose_KeepsNonStandardProperties()
    {
        var card = Card("item1.EMAIL;TYPE=INTERNET:a@b.c", "item1.X-ABLabel:Perso", "X-ABUID:ABC-DEF");
        var write = WriteWith(addresses: [new ContactWriteEmail(0, "a@b.c", "INTERNET")]);
        var output = VCardComposer.Compose(card, Uid, write);
        Assert.Contains("item1.X-ABLabel:Perso", output);
        Assert.Contains("X-ABUID:ABC-DEF", output);
    }
}
