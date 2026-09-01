using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
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

/// <summary>
/// The six group routes. Three statuses and no fourth: 200 or 204 when it worked, 400 for a body
/// that means nothing, 404 for an id this book does not hold — never 409, because a group write
/// carries no precondition to fail (décision 20).
/// </summary>
public sealed class ContactGroupsControllerTests
{
    private static readonly Guid Uid = Guid.NewGuid();

    private readonly Mock<IContactGroupStore> _store = new();

    private ContactGroupsController CreateController()
    {
        var controller = new ContactGroupsController(_store.Object);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid);
        return controller;
    }

    private static ContactGroupMembersRequest Batch(params Guid[] ids) => new(ids);

    [Fact]
    public async Task List_Returns200WithTheEnvelope()
    {
        var member = Guid.NewGuid();
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>()))
              .ReturnsAsync([new ContactGroupView(Guid.NewGuid(), "Amis", [member])]);

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ContactGroupsResponse>(ok.Value);
        Assert.Equal(member, Assert.Single(Assert.Single(body.Groups).MemberIds));
    }

    // 200 with the whole group, like POST /api/Contacts: the client caches what it just made.
    [Fact]
    public async Task Create_WhenAccepted_Returns200WithTheGroup()
    {
        var view = new ContactGroupView(Guid.NewGuid(), "Amis", []);
        _store.Setup(s => s.CreateAsync(Uid, "Amis", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(view));

        var result = await CreateController().Create(new ContactGroupRequest("Amis"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(view.Id, Assert.IsType<ContactGroupView>(ok.Value).Id);
    }

    [Fact]
    public async Task Create_WithNoName_Returns400AndNeverReachesTheStore()
    {
        var result = await CreateController().Create(new ContactGroupRequest("  "), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_WithNoBody_Returns400()
    {
        var result = await CreateController().Create(null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithAnOverLongName_Returns400()
    {
        var request = new ContactGroupRequest(new string('a', ContactValidator.MaxGroupNameLength + 1));

        var result = await CreateController().Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_AtTheCap_Returns400CarryingTheStoresReason()
    {
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<ContactGroupView>(ContactStore.CapReached));

        var result = await CreateController().Create(new ContactGroupRequest("Amis"), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(ContactStore.CapReached, Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Rename_WhenSaved_Returns204()
    {
        _store.Setup(s => s.RenameAsync(Uid, It.IsAny<Guid>(), "Collègues", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController().Rename(
            Guid.NewGuid(), new ContactGroupRequest("Collègues"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Rename_WithNoName_Returns400()
    {
        var result = await CreateController().Rename(
            Guid.NewGuid(), new ContactGroupRequest(null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // A group this book does not hold — another user's included — is an id that does not exist.
    [Fact]
    public async Task Rename_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.RenameAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(ContactStore.NotFound));

        var result = await CreateController().Rename(
            Guid.NewGuid(), new ContactGroupRequest("Amis"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_WhenRemoved_Returns204()
    {
        _store.Setup(s => s.DeleteAsync(Uid, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
    public async Task AddMembers_WhenSaved_Returns204AndPassesTheBatchThrough()
    {
        _store.Setup(s => s.AddMembersAsync(Uid, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());
        var group = Guid.NewGuid();

        var result = await CreateController().AddMembers(
            group, Batch(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.AddMembersAsync(
            Uid, group, It.Is<IReadOnlyList<Guid>>(v => v.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveMembers_WhenSaved_Returns204()
    {
        _store.Setup(s => s.RemoveMembersAsync(Uid, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController().RemoveMembers(
            Guid.NewGuid(), Batch(Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ContactsController.MaxBatch + 1)]
    public async Task Members_RefusesABatchThatIsEmptyOrOverTheCap(int size)
    {
        var ids = Enumerable.Range(0, size).Select(_ => Guid.NewGuid()).ToArray();
        var controller = CreateController();

        Assert.IsType<BadRequestObjectResult>(
            await controller.AddMembers(Guid.NewGuid(), Batch(ids), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(
            await controller.RemoveMembers(Guid.NewGuid(), Batch(ids), CancellationToken.None));
    }

    [Fact]
    public async Task Members_WithNoBody_Returns400()
    {
        var controller = CreateController();

        Assert.IsType<BadRequestObjectResult>(
            await controller.AddMembers(Guid.NewGuid(), null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(
            await controller.RemoveMembers(Guid.NewGuid(), null!, CancellationToken.None));
    }

    [Fact]
    public async Task Members_WhenTheGroupIsNotFound_Returns404()
    {
        _store.Setup(s => s.AddMembersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(ContactStore.NotFound));
        _store.Setup(s => s.RemoveMembersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(ContactStore.NotFound));
        var controller = CreateController();

        Assert.IsType<NotFoundObjectResult>(
            await controller.AddMembers(Guid.NewGuid(), Batch(Guid.NewGuid()), CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(
            await controller.RemoveMembers(Guid.NewGuid(), Batch(Guid.NewGuid()), CancellationToken.None));
    }

    // Décision 20: the state row's lock is what serialises two concurrent group writes, so there is
    // no precondition left for a caller to lose — no route here may ever answer 409.
    [Fact]
    public void NoGroupRoute_DeclaresAConflict()
    {
        var conflicts = typeof(ContactGroupsController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), false))
            .Cast<ProducesResponseTypeAttribute>()
            .Where(a => a.StatusCode == StatusCodes.Status409Conflict);

        Assert.Empty(conflicts);
    }
}
