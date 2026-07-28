using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text;
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
    public async Task Import_HandsTheStoreTheMappedRowsAndTheirVCard()
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
        Assert.Contains("TEL;TYPE=CELL:+32470000000", row.VCard);
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
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>()))
              .ReturnsAsync([new ContactView(Guid.NewGuid(), "Bruno", "Mertens", null, false, ["bruno@example.com"])]);

        var result = await CreateController().Export(CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("contacts-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);
        Assert.Contains("bruno@example.com", Encoding.UTF8.GetString(file.FileContents));
    }
}
