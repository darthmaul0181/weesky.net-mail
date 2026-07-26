using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class TrustedSendersControllerTests
{
    private readonly Mock<ITrustedSenderStore> _store = new();

    private TrustedSendersController CreateController()
    {
        var controller = new TrustedSendersController(_store.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
        return controller;
    }

    [Fact]
    public async Task List_Returns200WithTheAddresses()
    {
        _store.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(["news@example.com"]);

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(["news@example.com"], Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value));
    }

    [Fact]
    public async Task Add_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.AddAsync(It.IsAny<Guid>(), "news@example.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "news@example.com" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    // A message's FromAddress is always the bare address, so a decorated form must be unwrapped
    // before it is stored or the row could never match — and it would still eat a slot at the cap.
    [Fact]
    public async Task Add_WithADisplayName_StoresTheBareAddress()
    {
        _store.Setup(s => s.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "Alice Martin <alice@x.be>" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.AddAsync(It.IsAny<Guid>(), "alice@x.be", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Add_WithAnUnparsableAddress_Returns400AndNeverReachesTheStore()
    {
        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "not-an-address" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("That sender's address could not be read",
            Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _store.Verify(s => s.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Add_WithAQuotedLocalPartAndNoDomain_Returns400AndNeverReachesTheStore()
    {
        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "\"quoted@inside\"" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("That sender's address could not be read",
            Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _store.Verify(s => s.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Add_AtTheCap_Returns400CarryingTheStoreMessage()
    {
        _store.Setup(s => s.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(TrustedSenderStore.CapReached));

        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "news@example.com" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(TrustedSenderStore.CapReached, Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Remove_Returns204()
    {
        var result = await CreateController().Remove("news@example.com", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.RemoveAsync(It.IsAny<Guid>(), "news@example.com", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Idempotent for the reason DELETE /api/Mail/Attachments/{id} is: a 404 would confirm which
    // addresses this account has approved, and the caller can do nothing with the distinction.
    [Fact]
    public async Task Remove_UnknownAddress_StillReturns204()
    {
        _store.Setup(s => s.RemoveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var result = await CreateController().Remove("stranger@example.com", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Remove_WithNoAddress_Returns204AndNeverReachesTheStore()
    {
        var result = await CreateController().Remove("  ", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.RemoveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
