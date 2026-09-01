using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class ContactsControllerBulkTests
{
    private static readonly Guid Uid = Guid.NewGuid();

    private readonly Mock<IContactStore> _store = new();

    private ContactsController CreateController()
    {
        var controller = new ContactsController(_store.Object);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid);
        return controller;
    }

    [Fact]
    public async Task DeleteMany_AnswersNoContentAndPassesTheBatchThrough()
    {
        _store.Setup(s => s.DeleteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var controller = CreateController();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var result = await controller.DeleteMany(new BulkContactsRequest { Ids = ids }, default);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.DeleteManyAsync(It.IsAny<Guid>(), It.Is<IReadOnlyList<Guid>>(v => v.Count == 2), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Rien à supprimer n'est pas un succès muet : le client a envoyé une requête qui ne veut rien dire.
    [Fact]
    public async Task DeleteMany_RefusesAnEmptyBatch()
    {
        var controller = CreateController();

        var result = await controller.DeleteMany(new BulkContactsRequest { Ids = [] }, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteMany_RefusesOverTheCap()
    {
        var controller = CreateController();
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();

        var result = await controller.DeleteMany(new BulkContactsRequest { Ids = ids }, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // Le no-op silencieux est la règle du lot : zéro ligne touchée reste un 204.
    [Fact]
    public async Task DeleteMany_AnswersNoContentWhenNothingMatched()
    {
        _store.Setup(s => s.DeleteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var controller = CreateController();

        var result = await controller.DeleteMany(new BulkContactsRequest { Ids = [Guid.NewGuid()] }, default);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task SetFavoriteMany_PassesTheFlagThrough()
    {
        _store.Setup(s => s.SetFavoriteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var controller = CreateController();

        var result = await controller.SetFavoriteMany(
            new BulkFavoriteRequest { Ids = [Guid.NewGuid()], IsFavorite = true }, default);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.SetFavoriteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFavoriteMany_RefusesAnEmptyBatch()
    {
        var controller = CreateController();

        var result = await controller.SetFavoriteMany(new BulkFavoriteRequest { Ids = [] }, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
