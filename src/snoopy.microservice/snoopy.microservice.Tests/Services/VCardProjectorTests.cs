using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class VCardProjectorTests
{
    private const string Card30 = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Smith;John;Q.;Dr.;Jr.\r\n" +
        "FN:Dr. John Smith Jr.\r\nEMAIL;TYPE=INTERNET:john@work.example\r\n" +
        "item1.EMAIL;TYPE=INTERNET,HOME,PREF:john@home.example\r\n" +
        "item1.X-ABLabel:Perso\r\nTEL;TYPE=CELL:+32470000000\r\n" +
        "ADR;TYPE=HOME:PO 12;;Rue Haute 1;Bruxelles;;1000;Belgique\r\n" +
        "ORG:Acme;R&D;Lab\r\nBDAY:--03-15\r\nEND:VCARD\r\n";

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04];

    private static readonly string Base64Jpeg = Convert.ToBase64String(JpegBytes);

    private static readonly string Base64Svg =
        Convert.ToBase64String("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());

    private static string Card(params string[] lines) =>
        "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:X\r\n" + string.Join("\r\n", lines) + "\r\nEND:VCARD\r\n";

    private static string CardWithPhoto(string photoLine) => Card(photoLine);

    [Fact] // position = rang dans la carte ; pref extrait (TYPE=PREF vaut 1, absent vaut 101)
    public void Project_NumbersByCardOrderAndNormalisesPref()
    {
        var p = VCardProjector.Project(Card30);
        Assert.Equal([0, 1], p.Addresses.Select(a => a.Line.Position));
        Assert.Equal(101, p.Addresses[0].Line.Pref);
        Assert.Equal(1, p.Addresses[1].Line.Pref);
        Assert.Equal("item1", p.Addresses[1].Line.GroupName);
        Assert.Equal("TYPE=INTERNET,HOME,PREF", p.Addresses[1].Line.Params); // verbatim
    }

    [Fact] // N complet, ORG scindé en organization / department (composantes 2..n jointes par ;)
    public void Project_ReadsNamesAndOrg()
    {
        var p = VCardProjector.Project(Card30);
        Assert.Equal(("John", "Smith", "Q.", "Dr.", "Jr."),
            (p.FirstName, p.LastName, p.MiddleName, p.NamePrefix, p.NameSuffix));
        Assert.Equal("Acme", p.Organization);
        Assert.Equal("R&D;Lab", p.Department);
        Assert.Equal("--03-15", p.Birthday); // forme vCard telle quelle (décision 11)
    }

    // Défense en profondeur : plusieurs clients remplissent un N ou un FN vide d'un « ? ». Une
    // carte étrangère est stockée verbatim (décision 1), donc le garde est ici, à la lecture.
    [Theory]
    [InlineData("N:?;;;;")]
    [InlineData("N:?;?;;;")]
    [InlineData("N:?;?;?;?;?")]
    public void Project_PlaceholderName_ReadsAsNoNameAtAll(string line)
    {
        var p = VCardProjector.Project(Card(line));

        Assert.Null(p.FirstName);
        Assert.Null(p.LastName);
        Assert.Null(p.MiddleName);
        Assert.Null(p.NamePrefix);
        Assert.Null(p.NameSuffix);
    }

    [Fact] // FN:? est le même remplissage, sur la propriété obligatoire des deux versions
    public void Project_PlaceholderDisplayName_ReadsAsNull()
    {
        var p = VCardProjector.Project(
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:?\r\nN:?;;;;\r\nEND:VCARD\r\n");

        Assert.Null(p.DisplayName);
    }

    // La règle est stricte : un « ? » aux côtés d'un vrai nom est une donnée, pas un remplissage.
    [Fact]
    public void Project_QuestionMarkBesideARealName_IsKept()
    {
        var p = VCardProjector.Project(Card("N:?;Jean;;;"));

        Assert.Equal(("Jean", "?"), (p.FirstName, p.LastName));
    }

    [Fact] // décision 8, exception nommée : EMAIL invalide → ligne abandonnée, pas tronquée
    public void Project_DropsAnUnparsableEmail()
    {
        var p = VCardProjector.Project(Card("EMAIL:not-an-address", "EMAIL:ok@example.com"));
        var kept = Assert.Single(p.Addresses);
        Assert.Equal("ok@example.com", kept.Address);
        Assert.Equal(1, kept.Line.Position); // le rang carte est conservé, pas renuméroté
    }

    [Fact] // l'exception vaut aussi pour la longueur : > 320 → abandon entier, jamais tronqué
    public void Project_DropsAnOverlongEmailWhole()
    {
        var overlong = new string('a', 310) + "@example.com";
        var p = VCardProjector.Project(Card($"EMAIL:{overlong}", "EMAIL:ok@example.com"));
        var kept = Assert.Single(p.Addresses);
        Assert.Equal("ok@example.com", kept.Address);
        Assert.Equal(1, kept.Line.Position);
    }

    [Fact] // contrat de la colonne address : canonique — coupée des blancs, en minuscules
    public void Project_CanonicalisesTheEmailAddress()
    {
        var p = VCardProjector.Project(Card("EMAIL: John@Example.COM "));
        Assert.Equal("john@example.com", Assert.Single(p.Addresses).Address);
    }

    [Fact] // décision 4 : ADR portant une composante RFC 9554 → street ignorée (doublon)
    public void Project_IgnoresStreetWhenExtendedComponentsArePresent()
    {
        var p = VCardProjector.Project(
            Card("ADR;TYPE=HOME:PO 12;;Rue Haute 1;Bruxelles;;1000;Belgique;Room 5;;;;"));
        var adr = Assert.Single(p.PostalAddresses);
        Assert.Null(adr.Street);
        Assert.Equal("PO 12", adr.PoBox);
        Assert.Equal("Bruxelles", adr.Locality);
        Assert.Equal("Belgique", adr.Country);
    }

    [Fact] // décision 12 : data: projeté avec type MIME traduit ; http(s) jamais ; SVG jamais
    public void Project_TakesOnlyARasterDataPhoto()
    {
        var withData = VCardProjector.Project(CardWithPhoto("PHOTO;ENCODING=b;TYPE=JPEG:" + Base64Jpeg));
        Assert.Equal("image/jpeg", withData.Photo!.MediaType);
        Assert.Equal(JpegBytes, withData.Photo.Bytes);
        Assert.Null(VCardProjector.Project(CardWithPhoto("PHOTO;VALUE=URI:https://example.com/a.jpg")).Photo);
        Assert.Null(VCardProjector.Project(CardWithPhoto("PHOTO;ENCODING=b;TYPE=SVG:" + Base64Svg)).Photo);
    }

    [Fact] // décision 8 : un TYPE interminable est tronqué à 64, jamais fatal
    public void Project_TruncatesAnOversizeType()
    {
        var p = VCardProjector.Project(Card($"EMAIL;TYPE={new string('A', 200)}:a@b.cd"));
        var kept = Assert.Single(p.Addresses);
        Assert.Equal(64, kept.Line.Type.Length);
        Assert.Equal(new string('A', 64), kept.Line.Type);
    }

    [Fact] // multi-valué : jamais réduit à la première valeur (décision 4)
    public void Project_KeepsCommaJoinedComponents()
    {
        var p = VCardProjector.Project(Card("ADR;TYPE=HOME:;;Rue A\\,Rue B;Bruxelles;;;"));
        Assert.Equal("Rue A,Rue B", Assert.Single(p.PostalAddresses).Street);
    }

    [Fact] // décision 8 : une carte illisible rend une projection vide, jamais une exception
    public void Project_AnswersAnEmptyProjectionForAnUnreadableCard()
    {
        foreach (var raw in new[] { "this is not a vcard", "" })
        {
            var p = VCardProjector.Project(raw);
            Assert.Null(p.DisplayName);
            Assert.Null(p.Uid);
            Assert.Empty(p.Addresses);
            Assert.Empty(p.Phones);
            Assert.Empty(p.PostalAddresses);
            Assert.Null(p.Photo);
        }
    }

    [Fact] // params tronqués sur une frontière ';' hors guillemets : jamais coupés en plein milieu
    public void Project_TruncatesParamsOnASemicolonBoundary()
    {
        var padded = $"X-L=\"a;b\";TYPE=HOME;X-PAD={new string('z', 300)}";
        var p = VCardProjector.Project(Card($"EMAIL;{padded}:a@b.cd"));
        Assert.Equal("X-L=\"a;b\";TYPE=HOME", Assert.Single(p.Addresses).Line.Params);
    }

    [Fact] // params : 255 sur contact_emails, mais 512 sur contact_addresses (LABEL de 4.0 compris)
    public void Project_TruncatesParamsAtEachTablesOwnWidth()
    {
        var padded = $"TYPE=HOME;X-PAD={new string('z', 280)}";
        var p = VCardProjector.Project(Card($"EMAIL;{padded}:a@b.cd", $"ADR;{padded}:;;Rue;;;;"));
        Assert.Equal("TYPE=HOME", Assert.Single(p.Addresses).Line.Params);
        Assert.Equal(padded, Assert.Single(p.PostalAddresses).Line.Params);
    }

    [Fact] // décision 5 bis : une PREF numérique (4.0) est reprise telle quelle
    public void Project_ReadsANumericPrefAsIs()
    {
        var p = VCardProjector.Project(
            "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:X\r\nEMAIL;PREF=3:a@b.cd\r\nEND:VCARD\r\n");
        Assert.Equal(3, Assert.Single(p.Addresses).Line.Pref);
    }

    [Fact] // TYPE répété (licite en 4.0) : les valeurs sont jointes par ',', jamais réduites
    public void Project_JoinsRepeatedTypeParameters()
    {
        var p = VCardProjector.Project(Card("EMAIL;TYPE=HOME;TYPE=X-FOO:a@b.cd"));
        Assert.Equal("HOME,X-FOO", Assert.Single(p.Addresses).Line.Type);
    }

    [Fact] // décisions 5 et 12 : la première occurrence est la photo principale — inadmissible,
    public void Project_ProjectsNoPhotoWhenTheFirstOccurrenceIsInadmissible() // rien n'est projeté
    {
        var p = VCardProjector.Project(Card(
            "PHOTO;VALUE=URI:https://example.com/a.jpg",
            "PHOTO;ENCODING=b;TYPE=JPEG:" + Base64Jpeg));
        Assert.Null(p.Photo);
    }

    [Fact] // décision 12 : en 4.0 toute PHOTO est un URI ; c'est le schéma data: qui projette —
    public void Project_TakesAFourZeroDataUriPhoto() // et le type projeté est celui des octets
    {
        var p = VCardProjector.Project(
            "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:X\r\nPHOTO:data:image/png;base64," + Base64Jpeg + "\r\nEND:VCARD\r\n");
        Assert.Equal("image/jpeg", p.Photo!.MediaType); // reniflé (octets JPEG), pas le déclaré png
        Assert.Equal(JpegBytes, p.Photo.Bytes);
    }

    [Fact] // les octets décident : un TYPE=JPEG 3.0 posé sur du SVG ne projette rien
    public void Project_SniffsTheBytesAndRejectsNonRasterWhateverIsDeclared()
    {
        var p = VCardProjector.Project(CardWithPhoto("PHOTO;ENCODING=b;TYPE=JPEG:" + Base64Svg));
        Assert.Null(p.Photo);
    }

    [Fact] // un désalignement scanner/bibliothèque (AGENT 2.1) dégrade vers la vue de la
    public void Project_FallsBackToTheLibraryWhenTheRawScannerDesyncs() // bibliothèque, jamais en silence
    {
        var p = VCardProjector.Project(
            "BEGIN:VCARD\r\nVERSION:2.1\r\nFN:X\r\nAGENT:\r\nBEGIN:VCARD\r\nVERSION:2.1\r\n" +
            "N:Friday;Fred\r\nTEL;WORK:+1-213-555-1234\r\nEND:VCARD\r\n" +
            "TEL;TYPE=WORK,PREF:+321\r\nEND:VCARD\r\n");
        var phone = Assert.Single(p.Phones);
        Assert.Equal("+321", phone.Number);
        Assert.Equal(1, phone.Line.Pref);              // la PREF de la bibliothèque, pas 101
        Assert.Equal("WORK", phone.Line.Type);         // la classe de propriété survit
        Assert.Equal(string.Empty, phone.Line.Params); // seul le verbatim, colonne d'affichage, est perdu
    }

    [Fact] // le scanner brut et la collection de la bibliothèque s'alignent par rang document
    public void Project_AlignsVerbatimParamsWithTheLibraryByDocumentRank()
    {
        var p = VCardProjector.Project(Card(
            "EMAIL;TYPE=X-A:",                        // vide → abandonnée, mais consomme le rang 0
            "item9.EMAIL;TYPE=HO\r\n ME:b@example.com", // pliée en plein paramètre
            "EMAIL;TYPE=X-C:c@example.com"));
        Assert.Equal([1, 2], p.Addresses.Select(a => a.Line.Position));
        Assert.Equal(["TYPE=HOME", "TYPE=X-C"], p.Addresses.Select(a => a.Line.Params));
        Assert.Equal("item9", p.Addresses[0].Line.GroupName);
    }

    [Fact] // FN, NICKNAME, TITLE, NOTE, première URL, UID
    public void Project_ReadsScalarsAndUid()
    {
        var p = VCardProjector.Project(Card(
            "NICKNAME:Jo,Johnny", "TITLE:Boss", "NOTE:hello\\, world",
            "URL:https://one.example", "URL:https://two.example", "UID:urn:uuid:abc-123"));
        Assert.Equal("X", p.DisplayName);
        Assert.Equal("Jo,Johnny", p.Nickname);
        Assert.Equal("Boss", p.JobTitle);
        Assert.Equal("hello, world", p.Notes);
        Assert.Equal("https://one.example", p.Website);
        Assert.Equal("urn:uuid:abc-123", p.Uid);
    }

    // notes est TEXT (65 535 octets) : une NOTE plus longue passe sous le plafond de 1 Mo par
    // carte. Décision 8 tronque, elle ne refuse pas — sinon un import entier tombe en 500.
    [Fact]
    public void Project_TruncatesAnOverLongNote()
    {
        var note = new string('n', ContactValidator.MaxNotesLength + 500);

        var p = VCardProjector.Project(Card($"NOTE:{note}"));

        Assert.Equal(ContactValidator.MaxNotesLength, p.Notes!.Length);
    }

    [Fact] // décision 11 : même quand la bibliothèque typise la date, la forme carte est stockée
    public void Project_KeepsBirthdayAsWrittenWhenTheLibraryTypesIt()
    {
        Assert.Equal("1985-04-12", VCardProjector.Project(Card("BDAY:1985-04-12")).Birthday);
    }
}
