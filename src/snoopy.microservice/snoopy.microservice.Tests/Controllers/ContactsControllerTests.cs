using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
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
}
