using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class ContactsControllerConflictTests
{
    private static ContactsController NewController(IContactStore store)
    {
        var controller = new ContactsController(store);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Guid.NewGuid());
        return controller;
    }

    private static ContactRequest ValidRequest() =>
        new() { FirstName = "Bruno", Addresses = ["bruno@example.com"] };

    [Fact]
    public async Task AStaleHash_Answers409AndNot404()
    {
        var store = new Mock<IContactStore>();
        store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ContactStore.CardMoved));
        var controller = NewController(store.Object);

        var answer = await controller.Update(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        // Exact type: Conflict(body) is a ConflictObjectResult, never a bare ObjectResult. And 409
        // rather than 404 because the contact is very much there — it simply moved.
        Assert.IsType<ConflictObjectResult>(answer);
    }

    [Fact]
    public async Task AMissingContact_StillAnswers404()
    {
        var store = new Mock<IContactStore>();
        store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ContactStore.NotFound));
        var controller = NewController(store.Object);

        var answer = await controller.Update(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(answer);
    }

    [Fact]
    public async Task TheDetail_CarriesTheHashTheEditorMustSendBack()
    {
        var store = new Mock<IContactStore>();
        store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactStoreTestFactory.DetailWithHash("abc123"));
        var controller = NewController(store.Object);

        var answer = await controller.Get(Guid.NewGuid(), CancellationToken.None);

        // Without it on the way out, the editor has nothing to send back and the whole check is
        // unreachable from the only screen that needs it.
        var ok = Assert.IsType<OkObjectResult>(answer.Result);
        Assert.Equal("abc123", Assert.IsType<ContactDetail>(ok.Value).CardHash);
    }
}
