using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
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
    private const string NotServed = "Synchronisation is not available on this deployment";

    private readonly Mock<IDavCredentialStore> store = new();
    private readonly Mock<IDavAuthenticationCache> cache = new();
    private readonly Mock<IAuthAttemptThrottle> throttle = new();

    private DavCredentialsController CreateController(
        string? publicUrl = "https://api.mail.weesky.net", string username = "alice", string domain = "weesky.be")
    {
        var controller = new DavCredentialsController(
            store.Object, cache.Object, throttle.Object,
            Options.Create(new DavOptions { PublicUrl = publicUrl }),
            NullLogger<DavCredentialsController>.Instance)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext(username, domain, Uid)
        };
        return controller;
    }

    private static DavCredentialsView Body(ActionResult<DavCredentialsView> result) =>
        Assert.IsType<DavCredentialsView>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static string NotFoundMessage(ActionResult<DavCredentialsView> result) =>
        Assert.IsType<ResultEnveloppe>(Assert.IsType<NotFoundObjectResult>(result.Result).Value).Message!;

    private void ArrangeState(bool configured = true, bool enabled = true, DateTime? lastUsedAt = null) =>
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(configured, enabled, lastUsedAt));

    [Fact]
    public async Task Get_AnswersTheAddressFromConfigurationAndTheFullEmail()
    {
        // Never the host the request came in on: the frontend calls one URL, the proxy publishes
        // another, and the client is configured with the second.
        ArrangeState(lastUsedAt: new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc));

        var view = Body(await CreateController().Get(CancellationToken.None));

        Assert.Equal("https://api.mail.weesky.net", view.ServerUrl);
        Assert.Equal("alice@weesky.be", view.Username);
        Assert.True(view.Configured);
        Assert.True(view.CardDavEnabled);
        Assert.Equal(new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc), view.LastUsedAt);
    }

    [Fact]
    public async Task Get_TellsTheScreenTheUsernameTheServerIsKeyedOn()
    {
        // The field the user is asked to type into their client, so it is the canonical spelling
        // and not whatever casing the session was opened with.
        ArrangeState();

        var view = Body(await CreateController(username: "Alice", domain: "Weesky.BE").Get(CancellationToken.None));

        Assert.Equal("alice@weesky.be", view.Username);
    }

    [Fact]
    public async Task Get_NeverCarriesASecret()
    {
        // The assertion that keeps shut the door a "reveal" button would open.
        ArrangeState();

        var view = Body(await CreateController().Get(CancellationToken.None));

        Assert.Null(view.Password);
    }

    [Fact]
    public async Task Get_OnAnAccountThatNeverEnabled_AnswersOffRatherThan404()
    {
        // An absent row is "never enabled", which the screen draws as a switch in the off position.
        ArrangeState(configured: false, enabled: false);

        var view = Body(await CreateController().Get(CancellationToken.None));

        Assert.False(view.Configured);
        Assert.False(view.CardDavEnabled);
        Assert.Null(view.LastUsedAt);
    }

    [Fact]
    public void View_ToStringNeverPrintsTheSecret()
    {
        // The synthesised one would, and a debug log line on the view is how it reaches a file.
        var rendered = new DavCredentialsView(
            "https://api.mail.weesky.net", "alice@weesky.be", true, true, null, "ABCDEFGHIJKLMNOPQRST")
            .ToString();

        Assert.DoesNotContain("ABCDEFGHIJKLMNOPQRST", rendered);
        Assert.DoesNotContain("Password", rendered);
        Assert.Contains("alice@weesky.be", rendered);
    }

    [Fact]
    public async Task SetCardDav_TurningOnForTheFirstTime_AnswersTheSecretInTheSameResponse()
    {
        store.Setup(s => s.EnableAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("ABCDEFGHIJKLMNOPQRST");
        ArrangeState();

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle { Enabled = true }, CancellationToken.None));

        Assert.Equal("ABCDEFGHIJKLMNOPQRST", view.Password);
        Assert.True(view.CardDavEnabled);
    }

    [Fact]
    public async Task SetCardDav_TurningOnAgain_AnswersNoSecret()
    {
        // Including the concurrent-first-enable race, which the store answers as a re-enable:
        // never a 500 on the primary key, and never a second secret handed out.
        store.Setup(s => s.EnableAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        ArrangeState();

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle { Enabled = true }, CancellationToken.None));

        Assert.Null(view.Password);
    }

    [Fact]
    public async Task SetCardDav_TurningOff_KeepsTheAccountConfigured()
    {
        ArrangeState(enabled: false);

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle { Enabled = false }, CancellationToken.None));

        store.Verify(s => s.DisableAsync(Uid, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.EnableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(view.Configured);
        Assert.False(view.CardDavEnabled);
        Assert.Null(view.Password);
    }

    [Fact]
    public async Task SetCardDav_TurningOffAnAccountThatNeverEnabled_Is200AndCreatesNothing()
    {
        // The store is silent on a missing row, so turning off what was never on is not an error.
        ArrangeState(configured: false, enabled: false);

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle { Enabled = false }, CancellationToken.None));

        Assert.False(view.Configured);
        Assert.Null(view.Password);
        store.Verify(s => s.EnableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetCardDav_ForgetsTheCachedAuthenticationOnBothSidesOfTheSwitch()
    {
        // The cached entry carries the switch state: one outliving a movement answers with the
        // state from before it — a 200 after switching off, a 403 after switching on.
        ArrangeState();
        var controller = CreateController();

        await controller.SetCardDav(new DavSyncToggle { Enabled = true }, CancellationToken.None);
        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Once);

        await controller.SetCardDav(new DavSyncToggle { Enabled = false }, CancellationToken.None);
        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Exactly(2));
    }

    [Fact]
    public async Task Forget_NamesTheCanonicalIdentifierTheCacheIsKeyedOn()
    {
        // The cache compares byte for byte, so a Forget under another spelling of the same address
        // leaves the revoked secret working for the whole window.
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("TSRQPONMLKJIHGFEDCBA");
        ArrangeState();

        await CreateController(username: "Alice", domain: "Weesky.BE").Regenerate(CancellationToken.None);

        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Once);
    }

    [Fact]
    public void DavSyncToggle_ABodyThatNamesNoState_FailsToBindRatherThanReadingAsOff()
    {
        // {} once bound to false and switched synchronisation off; the required member makes the
        // formatter throw, which the API behaviour turns into the 400 the action declares.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DavSyncToggle>("{}", options));
    }

    [Fact]
    public async Task Regenerate_AnswersTheNewSecretAndForgetsTheCachedOne()
    {
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("TSRQPONMLKJIHGFEDCBA");
        ArrangeState();

        var view = Body(await CreateController().Regenerate(CancellationToken.None));

        Assert.Equal("TSRQPONMLKJIHGFEDCBA", view.Password);
        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Once);
    }

    [Fact]
    public async Task Regenerate_OnARowWhoseSwitchIsOff_StillAnswersTheNewSecret()
    {
        // The row is what regeneration needs; the switch is a separate state the store keys nothing
        // on, and the new secret takes effect the moment the switch goes back on.
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("TSRQPONMLKJIHGFEDCBA");
        ArrangeState(enabled: false);

        var view = Body(await CreateController().Regenerate(CancellationToken.None));

        Assert.Equal("TSRQPONMLKJIHGFEDCBA", view.Password);
        Assert.False(view.CardDavEnabled);
    }

    [Fact]
    public async Task Regenerate_OnAnAccountThatNeverEnabled_Is404()
    {
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var message = NotFoundMessage(await CreateController().Regenerate(CancellationToken.None));

        // Its own message: nothing to regenerate is not the same situation as nothing served here.
        Assert.Equal("Synchronisation has never been enabled", message);
        cache.Verify(c => c.Forget(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EveryAction_Is404WhenNoPublicAddressIsConfigured()
    {
        // No published address means no /dav to point a client at, and a secret generated for it
        // would promise a synchronisation nothing serves.
        var controller = CreateController(publicUrl: null);

        Assert.Equal(NotServed, NotFoundMessage(await controller.Get(CancellationToken.None)));
        Assert.Equal(NotServed, NotFoundMessage(
            await controller.SetCardDav(new DavSyncToggle { Enabled = true }, CancellationToken.None)));
        Assert.Equal(NotServed, NotFoundMessage(await controller.Regenerate(CancellationToken.None)));
        store.Verify(s => s.EnableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.VerifyNoOtherCalls();
    }
}
