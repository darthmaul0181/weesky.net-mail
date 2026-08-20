using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactCsvExporterTests
{
    private static ContactDetail Detail(
        Guid? id = null, string? first = null, string? last = null, string? nick = null,
        bool favorite = false, string? displayName = null,
        string? namePrefix = null, string? middleName = null,
        string? organization = null, string? department = null, string? jobTitle = null,
        string? notes = null, string? website = null, string? birthday = null,
        IReadOnlyList<ContactDetailPhone>? phones = null,
        IReadOnlyList<ContactDetailAddress>? postal = null,
        params string[] addresses) =>
        new(id ?? Guid.NewGuid(), first, last, nick, displayName, middleName, namePrefix, null,
            organization, department, jobTitle, birthday, website, notes, favorite, false,
            [.. addresses.Select((a, i) => new ContactDetailEmail(i, a, string.Empty, 101, string.Empty, string.Empty))],
            phones ?? [], postal ?? []);

    private static ContactWrite Write(
        string? first, string? last, string? nick, bool favorite, string source, params string[] addresses) =>
        new(first, last, nick, null, null, null, null, null, null, null, null, null, null,
            favorite, [.. addresses.Select(a => new ContactWriteEmail(null, a, string.Empty))], [], [], source);

    private static CsvDocument Parse(byte[] content) => CsvReader.Read(content);

    // Single-row documents only: the tests care about one contact's fields, not row order.
    private static string Field(CsvDocument document, string column) =>
        Assert.Single(document.Rows).Fields[document.Header.ToList().IndexOf(column)];

    private static readonly string[] FullHeader =
    [
        "Title", "First Name", "Middle Name", "Last Name", "Nick Name", "Display Name",
        "Company", "Department", "Job Title", "E-mail Address", "Notes", "Web Page", "Birthday",
        "Mobile Phone", "Home Phone", "Business Phone", "Home Fax", "Business Fax", "Other Phone",
        "Home Street", "Home City", "Home State", "Home Postal Code", "Home Country",
        "Business Street", "Business City", "Business State", "Business Postal Code", "Business Country",
        "Favorite",
    ];

    [Fact]
    public void Write_EmitsTheOutlookColumnSet3dReads()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(first: "Bruno", last: "Mertens", addresses: "bruno@example.com")]));

        Assert.Equal(FullHeader, document.Header);
        Assert.Equal(
        [
            "", "Bruno", "", "Mertens", "", "Bruno Mertens", "", "", "", "bruno@example.com",
            "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "",
        ],
            Assert.Single(document.Rows).Fields);

        // Décision 10: the column carries the card's own FN, never the fallback that flattens it.
        var withFn = Parse(ContactCsvExporter.Write(
            [Detail(first: "John", last: "Smith", displayName: "Dr. John Smith Jr.", addresses: "j@x.example")]));
        Assert.Equal("Dr. John Smith Jr.", Field(withFn, "Display Name"));
    }

    // A column empty across the whole file is noise, and a fixed ceiling would lose addresses.
    [Fact]
    public void Write_SizesTheAddressColumnsToTheFullestContact()
    {
        var document = Parse(ContactCsvExporter.Write(
        [
            Detail(first: "A", addresses: "a@example.com"),
            Detail(first: "B", addresses: ["b1@example.com", "b2@example.com", "b3@example.com"]),
        ]));

        var emailStart = document.Header.ToList().IndexOf("E-mail Address");
        Assert.Equal(
            ["E-mail Address", "E-mail 2 Address", "E-mail 3 Address"],
            document.Header.Skip(emailStart).Take(3));
    }

    [Fact]
    public void Write_MarksAFavourite()
    {
        var document = Parse(ContactCsvExporter.Write([Detail(first: "A", favorite: true, addresses: "a@example.com")]));

        Assert.Equal("true", Assert.Single(document.Rows).Fields[^1]);
    }

    // ExportAsync hands rows back in no particular order, so two contacts sharing a sort key need a
    // tiebreaker or the row order can drift between two exports of an unchanged book.
    [Fact]
    public void Write_BreaksATieOnDisplayNameByContactId()
    {
        var lower = Detail(id: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            first: "John", last: "Smith", addresses: "lower@example.com");
        var higher = Detail(id: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            first: "John", last: "Smith", addresses: "higher@example.com");

        var document = Parse(ContactCsvExporter.Write([higher, lower]));

        var emailIndex = document.Header.ToList().IndexOf("E-mail Address");
        Assert.Equal(["lower@example.com", "higher@example.com"], document.Rows.Select(r => r.Fields[emailIndex]));
    }

    // Written verbatim it would come back as a nickname on the next import, which is not a name the
    // user ever typed. The shape is the store's own: FallbackDisplayName ends on the first address,
    // so an address-only contact really does carry display_name = that address. A hand-built null
    // display name is a shape the store cannot produce, and a guard built on it never bites.
    [Fact]
    public void Write_LeavesTheDisplayNameEmptyForAnAddressOnlyContact()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(displayName: "a@example.com", addresses: "a@example.com")]));

        Assert.Equal("", Field(document, "Display Name"));
    }

    // The fold is on the value, not on its spelling: the column is canonical, the card's FN is not.
    [Fact]
    public void Write_LeavesTheDisplayNameEmptyWhenTheCardSpeltTheAddressDifferently()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(displayName: "A@Example.COM", addresses: "a@example.com")]));

        Assert.Equal("", Field(document, "Display Name"));
    }

    [Fact]
    public void Write_FallsBackToTheNicknameForTheDisplayName()
    {
        var document = Parse(ContactCsvExporter.Write([Detail(nick: "bruno", addresses: "a@example.com")]));

        Assert.Equal("bruno", Field(document, "Display Name"));
    }

    [Fact]
    public void Write_AnswersAHeaderOnlyFileForAnEmptyBook()
    {
        var document = Parse(ContactCsvExporter.Write([]));

        Assert.NotEmpty(document.Header);
        Assert.Empty(document.Rows);
    }

    // The mapping table: CELL first, then a fax combination, then a bare HOME/WORK, else Other —
    // and only the first phone in each bucket reaches the file, in the order the store handed them.
    // Every number here opens on '+' — the ordinary international prefix — and every one of them is
    // composed solely of digits and '+', so the phone-column exemption leaves them all bare (see
    // Write_LeavesAPlausiblePhoneNumberUnneutralised for the rule itself).
    [Fact]
    public void Write_MapsEveryPhoneTypeToItsOutlookColumn()
    {
        ContactDetailPhone[] phones =
        [
            new(0, "+32470000000", "CELL", 101, "", ""),
            new(1, "+3281000001", "HOME,VOICE", 101, "", ""),
            new(2, "+3281000002", "WORK,VOICE", 101, "", ""),
            new(3, "+3281000003", "HOME,FAX", 101, "", ""),
            new(4, "+3281000004", "WORK,FAX", 101, "", ""),
            new(5, "+3281000005", "PAGER", 101, "", ""),
            new(6, "+3281000009", "CELL", 101, "", ""), // excess CELL: stays in base, not in the file
        ];
        var document = Parse(ContactCsvExporter.Write([Detail(first: "A", phones: phones)]));

        Assert.Equal("+32470000000", Field(document, "Mobile Phone"));
        Assert.Equal("+3281000001", Field(document, "Home Phone"));
        Assert.Equal("+3281000002", Field(document, "Business Phone"));
        Assert.Equal("+3281000003", Field(document, "Home Fax"));
        Assert.Equal("+3281000004", Field(document, "Business Fax"));
        Assert.Equal("+3281000005", Field(document, "Other Phone"));
    }

    // The rule this addendum adds: a phone column exempts a value built solely from digits, spaces,
    // and the punctuation a phone number legitimately carries — it leaves for Outlook/Google without
    // the apostrophe our own importer would otherwise have to strip back off.
    [Fact]
    public void Write_LeavesAPlausiblePhoneNumberUnneutralised()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(first: "A", phones: [new ContactDetailPhone(0, "+32470000000", "CELL", 101, "", "")])]));

        Assert.Equal("+32470000000", Field(document, "Mobile Phone"));
    }

    // Parentheses, a dot and a slash are as legitimate in a phone number as digits and '+'. The
    // fixture opens on '+' deliberately — a value opening on '(' would fall through Neutralise
    // unchanged even with no phone exemption at all, so it alone would not pin this rule; dropping
    // any of space/'('/')'/'.'/'/' from the charset here sends the whole value to Neutralise, which
    // prefixes on the leading '+', and the assertion goes red.
    [Fact]
    public void Write_LeavesAPunctuatedPhoneNumberUnneutralised()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(first: "A", phones: [new ContactDetailPhone(0, "+32 (0)2 123.45.67 / 89", "CELL", 101, "", "")])]));

        Assert.Equal("+32 (0)2 123.45.67 / 89", Field(document, "Mobile Phone"));
    }

    // The case that proves the predicate is not "starts with a plausible character": every character
    // in this value would pass a first-char check, but the letters, '|', '!' and quotes past the
    // first are a DDE call, not a number, so the whole value must still be neutralised.
    [Fact]
    public void Write_NeutralisesAPhoneShapedDdePayload()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(first: "A", phones: [new ContactDetailPhone(0, "+cmd|' /C calc'!A0", "CELL", 101, "", "")])]));

        Assert.Equal("'+cmd|' /C calc'!A0", Field(document, "Mobile Phone"));
    }

    // A phone number really beginning with an apostrophe is outside the plausible-phone charset, so
    // it takes the ordinary Neutralise path and the pair stays lossless round-trip.
    [Fact]
    public void Write_KeepsAPhoneNumberThatReallyStartsWithAnApostropheAndTheImportUndoesIt()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(first: "A", phones: [new ContactDetailPhone(0, "'0470000000", "CELL", 101, "", "")])]));

        Assert.Equal("''0470000000", Field(document, "Mobile Phone"));
        var row = Assert.Single(ContactCsvMapper.Map(document).Value);
        Assert.Equal("'0470000000", row.Extras["mobilephone"]);
    }

    // Pinned so the phone exemption is never generalised to "any numeric-looking field": the
    // birthday's partial vCard form (e.g. "--0315") is a single field, our own import reads it back
    // losslessly whether neutralised or not, and no foreign importer knows to read that form anyway
    // — so it stays neutralised in block like every other non-phone free-text field.
    [Fact]
    public void Write_KeepsTheBirthdayNeutralisedDespiteLookingNumeric()
    {
        var document = Parse(ContactCsvExporter.Write([Detail(first: "A", birthday: "--0315")]));

        Assert.Equal("'--0315", Field(document, "Birthday"));
    }

    // Only the first HOME and the first WORK postal address reach the file; a second of either
    // stays on the card.
    [Fact]
    public void Write_MapsTheFirstHomeAndWorkPostalAddressToTheirColumns()
    {
        ContactDetailAddress[] postal =
        [
            new(0, "HOME", 101, "", "", null, null, "Rue X 1", "Namur", null, "5000", "Belgium"),
            new(1, "HOME", 101, "", "", null, null, "Rue X 2", "Elsewhere", null, "5001", "Belgium"),
            new(2, "WORK", 101, "", "", null, null, "Rue Y 2", "Brussels", "BXL", "1000", "Belgium"),
        ];
        var document = Parse(ContactCsvExporter.Write([Detail(first: "A", postal: postal)]));

        Assert.Equal("Rue X 1", Field(document, "Home Street"));
        Assert.Equal("Namur", Field(document, "Home City"));
        Assert.Equal("5000", Field(document, "Home Postal Code"));
        Assert.Equal("Belgium", Field(document, "Home Country"));
        Assert.Equal("Rue Y 2", Field(document, "Business Street"));
        Assert.Equal("Brussels", Field(document, "Business City"));
        Assert.Equal("BXL", Field(document, "Business State"));
    }

    // Every free-text field, the new ones included: company, department, job title, notes, the
    // postal components, and — now the mapper's strip is symmetric on every extras column, not just
    // the four name columns — phone numbers, Web Page and Birthday too. ContactValidator bounds
    // these only by length (phone <=64, website <=512, birthday <=64), never by charset, so an
    // unneutralised export would carry a live formula straight into the file; e-mail addresses are
    // the one field this deliberately skips, per Neutralise's own doc comment.
    [Fact]
    public void Write_NeutralisesAFormulaOnEveryFreeTextField()
    {
        ContactDetailAddress[] postal = [new(0, "HOME", 101, "", "", null, null, "=cmd", "+City", null, "-00000", "@Country")];
        ContactDetailPhone[] phones = [new(0, "=cmd|'/C calc'!A1", "CELL", 101, "", "")];
        var document = Parse(ContactCsvExporter.Write(
        [
            Detail(first: "A", organization: "=SUM(A1)", department: "+dept", jobTitle: "-title",
                notes: "@note", website: "=HYPERLINK(\"http://evil\")", birthday: "-00-00",
                phones: phones, postal: postal),
        ]));

        Assert.Equal("'=SUM(A1)", Field(document, "Company"));
        Assert.Equal("'+dept", Field(document, "Department"));
        Assert.Equal("'-title", Field(document, "Job Title"));
        Assert.Equal("'@note", Field(document, "Notes"));
        Assert.Equal("'=cmd|'/C calc'!A1", Field(document, "Mobile Phone"));
        Assert.Equal("'=HYPERLINK(\"http://evil\")", Field(document, "Web Page"));
        Assert.Equal("'-00-00", Field(document, "Birthday"));
        Assert.Equal("'=cmd", Field(document, "Home Street"));
        Assert.Equal("'+City", Field(document, "Home City"));
        Assert.Equal("'-00000", Field(document, "Home Postal Code"));
        Assert.Equal("'@Country", Field(document, "Home Country"));

        // And the mapper reads every one of them back to its raw value — the round trip the
        // asymmetric strip used to break for everything but the four name columns.
        var row = Assert.Single(ContactCsvMapper.Map(document).Value);
        Assert.Equal("=SUM(A1)", row.Extras["company"]);
        Assert.Equal("=cmd|'/C calc'!A1", row.Extras["mobilephone"]);
        Assert.Equal("=HYPERLINK(\"http://evil\")", row.Extras["webpage"]);
        Assert.Equal("-00-00", row.Extras["birthday"]);
        Assert.Equal("=cmd", row.Extras["homestreet"]);
    }

    // The claim the whole slice rests on: what we write, we read back — and reading it back a
    // second time creates nothing. The book carries an address-less contact and a nickname-only one
    // because those are the two the address index alone cannot recognise on the way back.
    [Fact]
    public async Task Write_RoundTripsThroughTheImport()
    {
        var db = nameof(Write_RoundTripsThroughTheImport);
        var user = Guid.NewGuid();
        var store = new ContactStore(new PreferencesTestDbContext(db));
        await store.CreateAsync(user, Write("Bruno", "Mertens", "bruno", true, "manual",
            "bruno@example.com", "second@example.com"), CancellationToken.None);
        await store.CreateAsync(user, Write("Solo", "Sansmail", null, false, "manual"),
            CancellationToken.None);
        await store.CreateAsync(user, Write(null, null, "zorro", false, "manual"),
            CancellationToken.None);

        var book = await new ContactStore(new PreferencesTestDbContext(db)).ExportAsync(user, CancellationToken.None);
        var mapped = ContactCsvMapper.Map(CsvReader.Read(ContactCsvExporter.Write(book)));
        Assert.True(mapped.IsSuccess);

        var outcome = await new ContactStore(new PreferencesTestDbContext(db)).ImportAsync(
            user,
            [.. mapped.Value.Select(r => new ContactImportRow(
                r.Line, r.FirstName, r.LastName, r.Nickname, r.IsFavorite, r.Addresses, null, null))],
            CancellationToken.None);

        Assert.Equal(0, outcome.Created);
        Assert.Equal(3, outcome.Merged);
        var after = await new ContactStore(new PreferencesTestDbContext(db)).ListAsync(user, CancellationToken.None);
        Assert.Equal(3, after.Count);
        var bruno = after.Single(c => c.FirstName == "Bruno");
        Assert.Equal("bruno", bruno.Nickname);
        Assert.True(bruno.IsFavorite);
        Assert.Equal(["bruno@example.com", "second@example.com"], bruno.Addresses);
        Assert.Contains(after, c => c.Nickname == "zorro");
    }

    private static ContactRequest RequestWithPhonesAndPostal() => new()
    {
        FirstName = "Bruno",
        LastName = "Mertens",
        IsFavorite = true,
        Addresses = ["bruno@example.com"],
        Phones =
        [
            new ContactPhonePayload { Number = "+32470000000", Type = "CELL" },
            new ContactPhonePayload { Number = "+3281000001", Type = "HOME,VOICE" },
        ],
        PostalAddresses =
        [
            new ContactAddressPayload
            {
                Type = "HOME", Street = "Rue X 1", Locality = "Namur",
                PostalCode = "5000", Country = "Belgium",
            },
        ],
    };

    private static async Task<FormFile> ExportToFile(ContactsController controller)
    {
        var exported = Assert.IsType<FileContentResult>(await controller.Export(CancellationToken.None));
        return new FormFile(new MemoryStream(exported.FileContents), 0, exported.FileContents.Length,
            "file", "contacts.csv");
    }

    // The realistic delivery path: export from one book, import into an EMPTY one, so the row
    // travels the create path and the file's phones/postal addresses are its only source of truth.
    // On the merge path ContactStore never reads row.Write's phones or postal addresses at all
    // (it composes a four-field MergeWrite and stops there) — asserting phones/postal after a merge
    // would pass even if the exporter wrote blank columns, which is exactly what the earlier version
    // of this test did without noticing.
    [Fact]
    public async Task Write_RoundTripsPhonesAndPostalAddressesThroughImportOfAFreshBook()
    {
        var db = nameof(Write_RoundTripsPhonesAndPostalAddressesThroughImportOfAFreshBook);
        var owner = Guid.NewGuid();
        var writer = new ContactsController(new ContactStore(new PreferencesTestDbContext(db)));
        writer.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("owner", "example.com", owner);
        await writer.Create(RequestWithPhonesAndPostal(), CancellationToken.None);
        var file = await ExportToFile(writer);

        var newOwner = Guid.NewGuid();
        var reader = new ContactsController(new ContactStore(new PreferencesTestDbContext(db)));
        reader.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("reader", "example.com", newOwner);
        var imported = await reader.Import(file, CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(imported.Result).Value);
        Assert.Equal(1, report.Created);
        Assert.Equal(0, report.Merged);
        Assert.Empty(report.Errors);

        var store = new ContactStore(new PreferencesTestDbContext(db));
        var createdId = Assert.Single(await store.ListAsync(newOwner, CancellationToken.None)).Id;
        var detail = await store.GetAsync(newOwner, createdId, CancellationToken.None);
        Assert.Equal(2, detail!.Phones.Count);
        Assert.Contains(detail.Phones, p => p.Number == "+32470000000");
        Assert.Contains(detail.Phones, p => p.Number == "+3281000001");
        Assert.Single(detail.PostalAddresses);
        Assert.Equal("Rue X 1", detail.PostalAddresses[0].Street);
        Assert.Equal("Namur", detail.PostalAddresses[0].Locality);
        Assert.Equal("Belgium", detail.PostalAddresses[0].Country);
    }

    // A separate claim from the one above: replaying the same export into the SAME book — the
    // merge path — changes nothing about the contact it already knows. Deliberately does not assert
    // phones/postal survived, since the merge path never reads them from the row at all.
    [Fact]
    public async Task Write_ReplayingTheSameExportMergesWithoutFailing()
    {
        var db = nameof(Write_ReplayingTheSameExportMergesWithoutFailing);
        var user = Guid.NewGuid();
        var controller = new ContactsController(new ContactStore(new PreferencesTestDbContext(db)));
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", user);
        await controller.Create(RequestWithPhonesAndPostal(), CancellationToken.None);
        var file = await ExportToFile(controller);

        var imported = await controller.Import(file, CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(imported.Result).Value);
        Assert.Equal(0, report.Created);
        Assert.Equal(1, report.Merged);
        Assert.Empty(report.Errors);
        Assert.Single(await new ContactStore(new PreferencesTestDbContext(db)).ListAsync(user, CancellationToken.None));
    }

    // A name a spreadsheet would evaluate goes out behind an apostrophe, and comes back as itself.
    [Fact]
    public void Write_NeutralisesAFormulaNameAndTheImportUndoesIt()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Detail(first: "=1+1", last: "@SUM", addresses: "a@example.com")]));

        Assert.Equal("'=1+1", Field(document, "First Name"));
        Assert.Equal("'@SUM", Field(document, "Last Name"));
        var row = Assert.Single(ContactCsvMapper.Map(document).Value);
        Assert.Equal("=1+1", row.FirstName);
        Assert.Equal("@SUM", row.LastName);
    }

    // The apostrophe is a trigger itself, or a name legitimately starting with one would be written
    // bare and read back one character shorter.
    [Fact]
    public void Write_KeepsANameThatReallyStartsWithAnApostrophe()
    {
        var document = Parse(ContactCsvExporter.Write([Detail(first: "'Tonio", addresses: "a@example.com")]));

        Assert.Equal("''Tonio", Field(document, "First Name"));
        Assert.Equal("'Tonio", Assert.Single(ContactCsvMapper.Map(document).Value).FirstName);
    }
}
