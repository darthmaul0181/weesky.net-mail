using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text;
using System.Text.Json;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class ContactsControllerTests
{
    // Fixed rather than a fresh Guid per call: the uid the controller hands the store is what
    // scopes an action to one user's book, so a test has to be able to name it.
    private static readonly Guid Uid = Guid.NewGuid();

    private readonly Mock<IContactStore> _store = new();

    private ContactsController CreateController()
    {
        var controller = new ContactsController(_store.Object);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid);
        return controller;
    }

    private static ContactRequest Valid() =>
        new() { FirstName = "Bruno", Addresses = ["bruno@example.com"] };

    [Fact]
    public async Task List_Returns200WithTheContacts()
    {
        var view = new ContactView(Guid.NewGuid(), "Bruno", "Mertens", null, false, ["bruno@example.com"]);
        _store.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([view]);

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ContactListResponse>(ok.Value);
        Assert.Equal("Bruno", Assert.Single(body.Contacts).FirstName);
    }

    [Fact]
    public async Task Create_WhenAccepted_Returns200WithTheId()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(id));

        var result = await CreateController().Create(Valid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, Assert.IsType<ContactView>(ok.Value).Id);
    }

    // The answer is built from the validated write, never echoed from the request: the store folds
    // and dedupes on the way in, so a raw echo would hand back a spelling the next read contradicts
    // — and the client caches this very object as the created contact.
    [Fact]
    public async Task Create_AnswersTheCanonicalisedDeduplicatedAddresses()
    {
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(Guid.NewGuid()));
        var request = new ContactRequest
        {
            FirstName = "Bruno",
            Addresses = ["Bruno@Example.COM", "bruno@example.com", "Other@Example.com"]
        };

        var result = await CreateController().Create(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(["bruno@example.com", "other@example.com"],
            Assert.IsType<ContactView>(ok.Value).Addresses);
    }

    // The validator's message must reach the client verbatim: it is what the form prints in its
    // error banner, so a generic 400 would leave the user with nothing to act on.
    [Fact]
    public async Task Create_WithNeitherNameNorAddress_Returns400()
    {
        var result = await CreateController().Create(new ContactRequest(), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        // Compared against the validator's own output rather than a copy of the sentence: a
        // generic "Invalid request" here would be a 400 the banner cannot act on.
        Assert.Equal(ContactValidator.Validate(new ContactRequest()).Error,
            Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WithAnUnparsableAddress_Returns400()
    {
        var request = new ContactRequest { FirstName = "Bruno", Addresses = ["nope"] };

        Assert.IsType<BadRequestObjectResult>((await CreateController().Create(request, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Create_AtTheCap_Returns400()
    {
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<Guid>(ContactStore.CapReached));

        Assert.IsType<BadRequestObjectResult>((await CreateController().Create(Valid(), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Update_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController().Update(Guid.NewGuid(), Valid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    // Not found and belonging to another user are the same answer on purpose: a 403 would confirm
    // the contact exists, and the namespace is sealed per user. That sealing is the uid the
    // controller hands down, so the call is verified on its arguments, not merely on its result.
    [Fact]
    public async Task Update_WhenNotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(ContactStore.NotFound));

        var result = await CreateController().Update(id, Valid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(Uid, id, It.IsAny<ContactWrite>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WithAnInvalidBody_Returns400()
    {
        var result = await CreateController().Update(Guid.NewGuid(), new ContactRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Delete_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        Assert.IsType<NoContentResult>(await CreateController().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(ContactStore.NotFound));

        Assert.IsType<NotFoundObjectResult>(await CreateController().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SetFavorite_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.SetFavoriteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), true,
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController()
            .SetFavorite(Guid.NewGuid(), new FavoriteRequest { IsFavorite = true }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task SetFavorite_WithNoBody_Returns400()
    {
        var result = await CreateController().SetFavorite(Guid.NewGuid(), null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static IFormFile FileOf(string csv)
    {
        var bytes = new UTF8Encoding(false).GetBytes(csv);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "contacts.csv");
    }

    private static IFormFile VCardFileOf(string text, string? mediaType = "text/vcard", bool bom = false)
    {
        var bytes = new UTF8Encoding(bom).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "contacts.vcf");
        if (mediaType != null) file.Headers = new HeaderDictionary { ["Content-Type"] = mediaType };
        return file;
    }

    private static string Card(params string[] lines) =>
        "BEGIN:VCARD\r\nVERSION:3.0\r\n" + string.Concat(lines.Select(l => l + "\r\n")) + "END:VCARD\r\n";


    [Fact]
    public async Task Import_Returns200WithTheReport()
    {
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(2, 1, 0, 0, []));

        var result = await CreateController().Import(
            FileOf("First Name,E-mail Address\r\nBruno,bruno@example.com"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<ContactImportReport>(ok.Value);
        Assert.Equal(2, report.Created);
        Assert.Equal(1, report.Merged);
    }

    [Fact]
    public async Task Import_HandsTheStoreTheMappedRowsAndTheirColumns()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        await CreateController().Import(
            FileOf("First Name,E-mail Address,Mobile Phone\r\nBruno,bruno@example.com,+32470000000"),
            CancellationToken.None);

        var row = Assert.Single(seen!);
        Assert.Equal("Bruno", row.FirstName);
        Assert.Equal(2, row.Line);
        var phone = Assert.Single(row.Write!.Phones);
        Assert.Equal("+32470000000", phone.Number);
        Assert.Equal("CELL", phone.Type);
    }

    // An address the file spelled wrong is dropped, not fatal — and the report has to say so.
    [Fact]
    public async Task Import_ReportsADroppedAddressWithoutFailingItsRow()
    {
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var result = await CreateController().Import(
            FileOf("First Name,E-mail Address,Other Email\r\nBruno,n/a,bruno@example.com"),
            CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, report.Created);
        Assert.Equal(0, report.Failed);
        Assert.Contains("n/a", Assert.Single(report.Errors).Reason);
    }

    // The same filler in two e-mail columns is one problem to the reader — and two identical entries
    // would also collide on the report list's React key.
    [Fact]
    public async Task Import_ReportsTheSameFillerInTwoColumnsOnce()
    {
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var result = await CreateController().Import(
            FileOf("First Name,E-mail Address,Other Email\r\nBruno,n/a,n/a"), CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, report.TotalErrors);
        Assert.Contains("n/a", Assert.Single(report.Errors).Reason);
    }

    // An over-long name is dropped, not truncated: truncating would store a fragment of whatever a
    // column shift spilled into it, which is exactly the scenario that produces the over-long value.
    [Fact]
    public async Task Import_DropsAnOverLongNameButStillImportsTheRow()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var result = await CreateController().Import(
            FileOf($"First Name,E-mail Address\r\n{new string('x', 150)},bruno@example.com"),
            CancellationToken.None);

        Assert.Null(Assert.Single(seen!).FirstName);
        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, report.Created);
        var error = Assert.Single(report.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("first name", error.Reason);
    }

    [Fact]
    public async Task Import_DropsAnOverLongAddressAndReportsIt()
    {
        var tooLong = new string('a', 400) + "@example.com";
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var result = await CreateController().Import(
            FileOf($"First Name,E-mail Address\r\nBruno,{tooLong}"), CancellationToken.None);

        Assert.Empty(Assert.Single(seen!).Addresses);
        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, report.Created);
        Assert.Contains("is not a valid e-mail address", Assert.Single(report.Errors).Reason);
    }

    // The clean failure this closes: the row's only content was an over-long name, so nothing
    // over-long ever reaches ImportAsync — what a real store would otherwise fail on with a 500.
    [Fact]
    public async Task Import_FailsCleanlyWhenTheOnlyContentIsAnOverLongName()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(0, 0, 0, 1, [new ContactImportError(2, "Neither a name nor a valid e-mail address")]));

        var result = await CreateController().Import(
            FileOf($"First Name\r\n{new string('x', 150)}"), CancellationToken.None);

        var row = Assert.Single(seen!);
        Assert.Null(row.FirstName);
        Assert.Empty(row.Addresses);
        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, report.Failed);
        Assert.Equal(2, report.TotalErrors);
        Assert.Contains(report.Errors, e => e.Reason == "Neither a name nor a valid e-mail address");
        Assert.Contains(report.Errors, e => e.Reason.Contains("first name"));
    }

    // The report's whole point: three different contributors — the store, a rejected address, and
    // an over-long name — land in one list, ordered by line, with the cap never hiding the total.
    [Fact]
    public async Task Import_MergesStoreMapperAndOverLongErrorsSortedByLine()
    {
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(3, 0, 0, 0, [new ContactImportError(2, "store issue")]));

        var result = await CreateController().Import(
            FileOf(
                "First Name,E-mail Address,Other Email\r\n" +
                "Alice,alice@example.com,\r\n" +
                "Bob,bob@example.com,not-an-address\r\n" +
                $"{new string('x', 150)},carol@example.com,"),
            CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(3, report.TotalErrors);
        Assert.Collection(report.Errors,
            e => { Assert.Equal(2, e.Line); Assert.Equal("store issue", e.Reason); },
            e => { Assert.Equal(3, e.Line); Assert.Contains("not-an-address", e.Reason); },
            e => { Assert.Equal(4, e.Line); Assert.Contains("first name", e.Reason); });
    }

    [Fact]
    public async Task Import_CapsTheErrorListAndCountsThemAll()
    {
        var many = Enumerable.Range(0, 60)
            .Select(i => new ContactImportError(i + 2, "bad")).ToList();
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(0, 0, 0, 60, many));

        var result = await CreateController().Import(
            FileOf("First Name\r\nBruno"), CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(60, report.TotalErrors);
        Assert.Equal(50, report.Errors.Count);
    }

    // The card is the truth, so it enters the book as it arrived — never re-serialised.
    [Fact]
    public async Task Import_SendsAVCardFileDownTheCardPath()
    {
        var first = Card("FN:Ana", "UID:card-1", "EMAIL:ana@example.com", "X-ABUID:ABC");
        var second = Card("N:Solo;Bo;;;", "FN:Bo Solo", "UID:card-2");
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(2, 0, 0, 0, []));

        var result = await CreateController().Import(VCardFileOf(first + second), CancellationToken.None);

        Assert.Equal(2, Assert.IsType<ContactImportReport>(
            Assert.IsType<OkObjectResult>(result.Result).Value).Created);
        Assert.Collection(seen!,
            r =>
            {
                Assert.Equal(1, r.Line);
                Assert.Equal(first, r.VCard);
                Assert.Equal("card-1", r.Uid);
                Assert.Equal("ana@example.com", Assert.Single(r.Addresses));
            },
            r =>
            {
                Assert.Equal(8, r.Line);
                Assert.Equal(second, r.VCard);
                Assert.Equal("Bo", r.FirstName);
            });
    }

    // A file picker that names no media type, and the BOM a Windows editor leaves in front.
    [Fact]
    public async Task Import_RoutesOnTheContentWhenNoMediaTypeSaysSo()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        await CreateController().Import(
            VCardFileOf(Card("FN:Ana", "EMAIL:ana@example.com"), mediaType: null, bom: true),
            CancellationToken.None);

        Assert.Equal(Card("FN:Ana", "EMAIL:ana@example.com"), Assert.Single(seen!).VCard);
    }

    // A card past the ceiling is a line in error, exactly like a skipped CSV row.
    [Fact]
    public async Task Import_ReportsACardPastTheSizeCapWithItsLine()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var result = await CreateController().Import(
            VCardFileOf(Card("FN:Ana", "EMAIL:ana@example.com")
                + Card("FN:Bo", "NOTE:" + new string('x', 1024 * 1024))),
            CancellationToken.None);

        Assert.Equal(Card("FN:Ana", "EMAIL:ana@example.com"), Assert.Single(seen!).VCard);
        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var error = Assert.Single(report.Errors);
        Assert.Equal(6, error.Line);
        Assert.Equal(ContactStore.CardTooLarge, error.Reason);
    }

    // Truncating a UID would forge a synchronisation identity the card does not carry.
    [Fact]
    public async Task Import_ReportsAnOverLongUidWithItsLine()
    {
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(0, 0, 0, 0, []));

        var result = await CreateController().Import(
            VCardFileOf(Card("FN:Ana", "UID:" + new string('u', 256))), CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var error = Assert.Single(report.Errors);
        Assert.Equal(1, error.Line);
        Assert.Contains("UID", error.Reason);
    }

    // A fragment is a line in error carrying its own line, never a stored card: an END-less vCard
    // is what 4c would then serve.
    [Fact]
    public async Task Import_ReportsAMalformedCardWithItsLine()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var result = await CreateController().Import(
            VCardFileOf(Card("FN:Ana", "EMAIL:ana@example.com") + "BEGIN:VCARD\r\nFN:Bo\r\n"),
            CancellationToken.None);

        Assert.Single(seen!);
        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var error = Assert.Single(report.Errors);
        Assert.Equal(6, error.Line);
        Assert.Equal(ContactsController.CardIncomplete, error.Reason);
    }

    // Every accented name of an Outlook or phone export, and a card kept verbatim keeps whatever
    // the decoding made of it: a replacement character here is data destroyed for good.
    [Fact]
    public async Task Import_ReadsALatin1CardWithoutMangingItsAccents()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var card = Card("N:Mertens;Amélie;;;", "FN:Amélie Mertens", "EMAIL:a@example.com");
        var bytes = Encoding.Latin1.GetBytes(card);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "contacts.vcf");

        await CreateController().Import(file, CancellationToken.None);

        var row = Assert.Single(seen!);
        Assert.Equal("Amélie", row.FirstName);
        Assert.Equal(card, row.VCard);
    }

    [Fact]
    public async Task Import_Returns400WhenAVCardFileCarriesNoCard()
    {
        var result = await CreateController().Import(
            VCardFileOf("nothing here\r\n"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.ImportAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ContactImportRow>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // Every column the tables do not model, on the write the store composes the card from — the
    // mapping table ContactVCardWriter used to own. What the composer makes of it is pinned by
    // VCardComposerTests.ComposeNew_EmitsEveryFamilyOfANewCard, on these very values.
    [Fact]
    public async Task Import_MapsTheCsvColumnsOutsideTheModelOntoTheWrite()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        await CreateController().Import(FileOf(
            "First Name,Last Name,Middle Name,Title,E-mail Address,Mobile Phone,Home Phone," +
            "Business Fax,Company,Department,Job Title,Notes,Birthday,Web Page," +
            "Home Street,Home City,Home Postal Code,Home Country,Business Street,Office Location\r\n" +
            "Bruno,Mertens,J,Mr,bruno@example.com,+32470000000,+3281000000,+3281000001," +
            "Weesky,Support,Engineer,a note,1980-01-15,https://x.be," +
            "Rue X 1,Namur,5000,Belgium,Rue Y 2,Room 3"),
            CancellationToken.None);

        var write = Assert.Single(seen!).Write!;
        Assert.Equal(
            [("+32470000000", "CELL"), ("+3281000000", "HOME,VOICE"), ("+3281000001", "WORK,FAX")],
            write.Phones.Select(p => (p.Number, p.Type)));
        Assert.Equal("Weesky", write.Organization);
        Assert.Equal("Support", write.Department);
        Assert.Equal("Engineer", write.JobTitle);
        Assert.Equal("a note", write.Notes);
        Assert.Equal("1980-01-15", write.Birthday);
        Assert.Equal("https://x.be", write.Website);
        Assert.Equal("J", write.MiddleName);
        Assert.Equal("Mr", write.NamePrefix); // Outlook's "Title" is the honorific, N's fourth part
        Assert.Collection(write.PostalAddresses,
            home =>
            {
                Assert.Equal("HOME", home.Type);
                Assert.Null(home.Extended);
                Assert.Equal("Rue X 1", home.Street);
                Assert.Equal("Namur", home.Locality);
                Assert.Equal("5000", home.PostalCode);
                Assert.Equal("Belgium", home.Country);
                Assert.Null(home.Region);
            },
            work =>
            {
                Assert.Equal("WORK", work.Type);
                Assert.Equal("Room 3", work.Extended);
                Assert.Equal("Rue Y 2", work.Street);
            });
    }

    // A reader never names an identity: minting one here would put a GUID of our making on an
    // existing contact the row merges into, rotating the very key a CardDAV client syncs on.
    [Fact]
    public async Task Import_MintsNoIdentityForACsvRow()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        await CreateController().Import(FileOf("First Name\r\nBruno"), CancellationToken.None);

        var row = Assert.Single(seen!);
        Assert.Null(row.Uid);
        Assert.Null(row.VCard);
        Assert.Equal("Bruno", row.Write!.FirstName);
    }

    // The request ceiling a vCard file needs, and the one the attribute has to carry itself:
    // model binding buffers the body before a line of this controller runs.
    [Fact]
    public void Import_RefusesARequestPastTwentyMegabytes()
    {
        var attribute = typeof(ContactsController).GetMethod(nameof(ContactsController.Import))!
            .GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute));

        Assert.Equal(20L * 1024 * 1024, attribute.ConstructorArguments[0].Value);
    }

    [Fact]
    public async Task Import_Returns400WithoutAFile()
    {
        var result = await CreateController().Import(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // What catches the file read with the wrong delimiter and the one that is not a CSV at all.
    [Fact]
    public async Task Import_Returns400WhenNoColumnIsRecognised()
    {
        var result = await CreateController().Import(FileOf("Alpha,Beta\r\n1,2"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.ImportAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ContactImportRow>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Export_AnswersACsvAttachment()
    {
        _store.Setup(s => s.ExportAsync(Uid, It.IsAny<CancellationToken>()))
              .ReturnsAsync([DetailOf(addresses: ["bruno@example.com"])]);

        var result = await CreateController().Export(CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("contacts-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);
        Assert.Contains("bruno@example.com", Encoding.UTF8.GetString(file.FileContents));
    }

    private static ContactDetail DetailOf(
        Guid? id = null, string? first = "Bruno", string? last = "Mertens",
        IReadOnlyList<string>? addresses = null) =>
        new(id ?? Guid.NewGuid(), first, last, null, null, null, null, null, null, null, null,
            null, null, null, false, false,
            [.. (addresses ?? []).Select((a, i) => new ContactDetailEmail(i, a, string.Empty, 101, string.Empty, string.Empty))],
            [], []);

    // A foreign id is not distinguishable from an unknown one: the store answers null either way,
    // and 403 would confirm the id exists.
    [Fact]
    public async Task Get_AnswersTheDetailAndHidesForeignIds()
    {
        var id = Guid.NewGuid();
        var detail = DetailOf(id: id);
        _store.Setup(s => s.GetAsync(Uid, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((ContactDetail?)null);
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(detail);

        var found = await CreateController().Get(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(found.Result);
        Assert.Same(detail, ok.Value);

        var foreign = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(foreign.Result);
    }

    [Fact]
    public async Task GetPhoto_HonoursIfNoneMatch()
    {
        var id = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        _store.Setup(s => s.GetPhotoAsync(Uid, id, It.IsAny<CancellationToken>()))
              .ReturnsAsync((bytes, "image/jpeg", "abc123"));

        var first = CreateController();
        var firstResult = await first.GetPhoto(id, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(firstResult);
        Assert.Equal(bytes, file.FileContents);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("\"abc123\"", first.Response.Headers.ETag.ToString());
        Assert.Equal("nosniff", first.Response.Headers.XContentTypeOptions.ToString());

        var second = CreateController();
        second.Request.Headers.IfNoneMatch = "\"abc123\"";
        var secondResult = await second.GetPhoto(id, CancellationToken.None);

        Assert.IsType<StatusCodeResult>(secondResult);
        Assert.Equal(StatusCodes.Status304NotModified, ((StatusCodeResult)secondResult).StatusCode);
    }

    [Fact]
    public async Task GetPhoto_ForAForeignOrMissingIdReturns404()
    {
        _store.Setup(s => s.GetPhotoAsync(Uid, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(((byte[] Bytes, string MediaType, string CardHash)?)null);

        var result = await CreateController().GetPhoto(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // params/group_name/pref cannot bind onto ContactRequest or its line payloads at all — the
    // decision is that they never enter, not that something filters them out. The behavioural
    // assertions below (the write only ever carries the modelled fields) would pass identically
    // whether or not that were true, so the structural check above them is the one that actually
    // pins it: if a future edit ever added a Params/GroupName/Pref property to a payload type, only
    // this reflection check — not the JSON deserialisation below — would catch it.
    [Fact]
    public async Task Put_IgnoresOutputOnlyFields()
    {
        string[] outputOnly = ["Params", "GroupName", "Pref"];
        foreach (var payload in new[]
                 { typeof(ContactEmailPayload), typeof(ContactPhonePayload), typeof(ContactAddressPayload) })
            Assert.Empty(payload.GetProperties().Select(p => p.Name).Intersect(outputOnly));

        ContactWrite? seen = null;
        _store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, Guid, ContactWrite, CancellationToken>((_, _, write, _) => seen = write)
              .ReturnsAsync(Result.Success());
        var json = """
            {
              "firstName": "Bruno",
              "addresses": [{"position": 0, "address": "bruno@example.com", "type": "HOME",
                              "params": "TYPE=HOME;PREF=1", "pref": 1, "groupName": "item1"}],
              "phones": [{"position": 0, "number": "+32470000000", "type": "CELL",
                          "params": "TYPE=CELL", "pref": 1, "groupName": "item2"}],
              "postalAddresses": [{"position": 0, "type": "HOME", "street": "Rue X",
                                    "params": "TYPE=HOME", "pref": 1, "groupName": "item3"}]
            }
            """;
        var request = JsonSerializer.Deserialize<ContactRequest>(json, Web)!;

        var result = await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("bruno@example.com", Assert.Single(seen!.Addresses).Address);
        Assert.Equal("+32470000000", Assert.Single(seen.Phones).Number);
        Assert.Equal("Rue X", Assert.Single(seen.PostalAddresses).Street);
    }

    // The POST answer is rebuilt from the validated write, never re-read from the store — new
    // fields (DisplayName) travel the same way the address list already did, and HasPhoto is always
    // false: 4a gives the photo no write door (décision 12).
    [Fact]
    public async Task Create_AnswersFromTheValidatedWrite()
    {
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(Guid.NewGuid()));
        var request = new ContactRequest
        {
            FirstName = "Bruno",
            DisplayName = "Dr. Bruno Mertens",
            Addresses = ["bruno@example.com"],
        };

        var result = await CreateController().Create(request, CancellationToken.None);

        var view = Assert.IsType<ContactView>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Dr. Bruno Mertens", view.DisplayName);
        Assert.False(view.HasPhoto);
    }
}
