using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

/// <summary>
/// The seam 4c-i named and could not close: a regeneration puts every configured device into a
/// failure loop, and once the identifier is blocked the CORRECT new secret answers 429.
/// </summary>
public sealed class AuthAttemptThrottleSeamTests
{
    private const string Address = "203.0.113.7";
    private const string AuthenticatedEmail = "alice@weesky.be";

    private static readonly Guid Uid = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly Mock<IDavCredentialStore> store = new();
    private readonly Mock<IDavAuthenticationCache> cache = new();

    public AuthAttemptThrottleSeamTests()
    {
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ABCDEFGHIJKLMNOPQRST");
        store.Setup(s => s.EnableAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ABCDEFGHIJKLMNOPQRST");
    }

    private static AuthAttemptThrottle BlockedOn(string identifier, string? address)
    {
        var throttle = new AuthAttemptThrottle(TimeProvider.System);
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++) throttle.RecordFailure(identifier, address);

        // Never assumed: a battery that never reached the blocked state would prove nothing at all.
        Assert.True(throttle.IsBlocked(identifier, address, out _));
        return throttle;
    }

    private DavCredentialsController NewCredentialsController(
        IAuthAttemptThrottle throttle, string? publicUrl = "https://api.mail.weesky.net") =>
        new(store.Object, cache.Object, throttle,
            Options.Create(new DavOptions { PublicUrl = publicUrl }),
            NullLogger<DavCredentialsController>.Instance)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", Uid)
        };

    [Fact]
    public void ForgettingAnIdentifier_ClearsItsFailures()
    {
        var throttle = BlockedOn(AuthenticatedEmail, Address);

        throttle.ForgetIdentifier(AuthenticatedEmail);

        // The caller just proved its identity with a JWT — a factor the throttle does not guard.
        // Without this seam, a user who regenerates while two devices are still syncing locks
        // themselves out, and the CORRECT new secret answers 429. Read without an address, so the
        // surviving address key cannot make this pass for the wrong reason.
        Assert.False(throttle.IsBlocked(AuthenticatedEmail, null, out _));
    }

    [Fact]
    public void ForgettingAnIdentifier_LeavesTheAddressKeyAlone()
    {
        // An attacker sharing the victim's /64 must not be able to unblock themselves by making
        // somebody else regenerate. Asserted on an identifier that never failed, so the address
        // key is the only thing that can still be blocking.
        var throttle = BlockedOn(AuthenticatedEmail, Address);

        throttle.ForgetIdentifier(AuthenticatedEmail);

        Assert.True(throttle.IsBlocked("never-tried@weesky.be", Address, out _));
    }

    [Fact]
    public void ForgettingAnIdentifier_LeavesAnotherIdentifiersFailuresAlone()
    {
        var throttle = BlockedOn("someone-else@weesky.be", Address);

        throttle.ForgetIdentifier(AuthenticatedEmail);

        Assert.True(throttle.IsBlocked("someone-else@weesky.be", null, out _));
    }

    [Fact]
    public void ForgettingIsCanonicalisedLikeEveryOtherEntryPoint()
    {
        var throttle = BlockedOn("Alice@Weesky.BE", Address);

        throttle.ForgetIdentifier("  alice@weesky.be  ");

        // IdentifierKey trims and lowercases; a Forget that did not would silently do nothing.
        Assert.False(throttle.IsBlocked("Alice@Weesky.BE", null, out _));
    }

    [Fact]
    public async Task Regenerating_ForgetsTheIdentifiersFailures()
    {
        var throttle = new Mock<IAuthAttemptThrottle>();
        var controller = NewCredentialsController(throttle.Object);

        await controller.Regenerate(CancellationToken.None);

        throttle.Verify(t => t.ForgetIdentifier(IdentityResolver.Canonical(AuthenticatedEmail)),
            Times.Once);
    }

    [Fact]
    public async Task Regenerating_ForgetsUnderTheSpellingTheThrottleIsKeyedOn()
    {
        // The session may have been opened under any casing; the throttle keys on the canonical
        // one, and a Forget spelled otherwise would clear a key nothing ever wrote to.
        var throttle = new Mock<IAuthAttemptThrottle>();
        var controller = NewCredentialsController(throttle.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("Alice", "Weesky.BE", Uid);

        await controller.Regenerate(CancellationToken.None);

        throttle.Verify(t => t.ForgetIdentifier(AuthenticatedEmail), Times.Once);
    }

    [Fact]
    public async Task RegeneratingWhatWasNeverEnabled_ForgetsNothing()
    {
        // The 404 branch: nothing was rotated, so no device is in a failure loop and there is no
        // reason to hand the caller a cleared counter.
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var throttle = new Mock<IAuthAttemptThrottle>();

        await NewCredentialsController(throttle.Object).Regenerate(CancellationToken.None);

        throttle.Verify(t => t.ForgetIdentifier(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TurningSyncOn_ForgetsThemToo()
    {
        var throttle = new Mock<IAuthAttemptThrottle>();
        var controller = NewCredentialsController(throttle.Object);

        await controller.SetCardDav(new DavSyncToggle { Enabled = true }, CancellationToken.None);

        // Enabling for the first time also mints a secret, so it lands the user in the same shape.
        throttle.Verify(t => t.ForgetIdentifier(IdentityResolver.Canonical(AuthenticatedEmail)),
            Times.Once);
    }

    [Fact]
    public async Task TurningSyncOff_LeavesTheFailuresAlone()
    {
        // Switching off ends the failure loop by itself; loosening the throttle there would widen
        // the seam past what the JWT actually justifies.
        var throttle = new Mock<IAuthAttemptThrottle>();
        var controller = NewCredentialsController(throttle.Object);

        await controller.SetCardDav(new DavSyncToggle { Enabled = false }, CancellationToken.None);

        throttle.Verify(t => t.ForgetIdentifier(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnADeploymentServingNoSynchronisation_NothingIsForgotten()
    {
        var throttle = new Mock<IAuthAttemptThrottle>();
        var controller = NewCredentialsController(throttle.Object, publicUrl: null);

        await controller.Regenerate(CancellationToken.None);
        await controller.SetCardDav(new DavSyncToggle { Enabled = true }, CancellationToken.None);

        throttle.Verify(t => t.ForgetIdentifier(It.IsAny<string>()), Times.Never);
    }
}
