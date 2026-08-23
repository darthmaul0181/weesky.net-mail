using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class DavCredentialsControllerTests
{
    private static readonly Guid Uid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IDavCredentialStore> store = new();
    private readonly Mock<IDavAuthenticationCache> cache = new();

    private DavCredentialsController CreateController(
        string? publicUrl = "https://api.mail.weesky.net", string username = "alice", string domain = "weesky.be")
    {
        var controller = new DavCredentialsController(
            store.Object, cache.Object,
            Options.Create(new DavOptions { PublicUrl = publicUrl }),
            NullLogger<DavCredentialsController>.Instance)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext(username, domain, Uid)
        };
        return controller;
    }

    private static DavCredentialsView Body(ActionResult<DavCredentialsView> result) =>
        Assert.IsType<DavCredentialsView>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task Get_AnswersTheAddressFromConfigurationAndTheFullEmail()
    {
        // Never the host the request came in on: the frontend calls one URL, the proxy publishes
        // another, and the client is configured with the second.
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc)));

        var view = Body(await CreateController().Get(CancellationToken.None));

        Assert.Equal("https://api.mail.weesky.net", view.ServerUrl);
        Assert.Equal("alice@weesky.be", view.Username);
        Assert.True(view.Configured);
        Assert.True(view.CardDavEnabled);
        Assert.Equal(new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc), view.LastUsedAt);
    }

    [Fact]
    public async Task Get_NeverCarriesASecret()
    {
        // The assertion that keeps shut the door a "reveal" button would open.
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().Get(CancellationToken.None));

        Assert.Null(view.Password);
    }

    [Fact]
    public async Task SetCardDav_TurningOnForTheFirstTime_AnswersTheSecretInTheSameResponse()
    {
        store.Setup(s => s.EnableAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("ABCDEFGHIJKLMNOPQRST");
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle(true), CancellationToken.None));

        Assert.Equal("ABCDEFGHIJKLMNOPQRST", view.Password);
        Assert.True(view.CardDavEnabled);
    }

    [Fact]
    public async Task SetCardDav_TurningOnAgain_AnswersNoSecret()
    {
        // Including the concurrent-first-enable race, which the store answers as a re-enable:
        // never a 500 on the primary key, and never a second secret handed out.
        store.Setup(s => s.EnableAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle(true), CancellationToken.None));

        Assert.Null(view.Password);
    }

    [Fact]
    public async Task SetCardDav_TurningOff_KeepsTheAccountConfigured()
    {
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, false, null));

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle(false), CancellationToken.None));

        store.Verify(s => s.DisableAsync(Uid, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.EnableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(view.Configured);
        Assert.False(view.CardDavEnabled);
        Assert.Null(view.Password);
    }

    [Fact]
    public async Task SetCardDav_ForgetsTheCachedAuthenticationOnBothSidesOfTheSwitch()
    {
        // The cached entry carries the switch state: one outliving a movement answers with the
        // state from before it — a 200 after switching off, a 403 after switching on.
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));
        var controller = CreateController();

        await controller.SetCardDav(new DavSyncToggle(true), CancellationToken.None);
        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Once);

        await controller.SetCardDav(new DavSyncToggle(false), CancellationToken.None);
        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Exactly(2));
    }

    [Fact]
    public async Task Forget_NamesTheCanonicalIdentifierTheCacheIsKeyedOn()
    {
        // The cache compares byte for byte, so a Forget under another spelling of the same address
        // leaves the revoked secret working for the whole window.
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("TSRQPONMLKJIHGFEDCBA");
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        await CreateController(username: "Alice", domain: "Weesky.BE").Regenerate(CancellationToken.None);

        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Once);
    }

    [Fact]
    public async Task Regenerate_AnswersTheNewSecretAndForgetsTheCachedOne()
    {
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("TSRQPONMLKJIHGFEDCBA");
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().Regenerate(CancellationToken.None));

        Assert.Equal("TSRQPONMLKJIHGFEDCBA", view.Password);
        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Once);
    }

    [Fact]
    public async Task Regenerate_OnAnAccountThatNeverEnabled_Is404()
    {
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var result = await CreateController().Regenerate(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task EveryAction_Is404WhenNoPublicAddressIsConfigured()
    {
        // No published address means no /dav to point a client at, and a secret generated for it
        // would promise a synchronisation nothing serves.
        var controller = CreateController(publicUrl: null);

        Assert.IsType<NotFoundObjectResult>((await controller.Get(CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>(
            (await controller.SetCardDav(new DavSyncToggle(true), CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Regenerate(CancellationToken.None)).Result);
        store.Verify(s => s.EnableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.VerifyNoOtherCalls();
    }
}
