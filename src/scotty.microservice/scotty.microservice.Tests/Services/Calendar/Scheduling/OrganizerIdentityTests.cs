using Moq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Platform;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar.Scheduling;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Scheduling;

public sealed class OrganizerIdentityTests
{
    private readonly Mock<ISendingIdentityStore> _identities = new();
    private readonly Mock<IAliasDirectory> _aliases = new();
    private readonly Mock<IProfileReader> _profiles = new();
    private readonly User _user = new("alice@weesky.be") { WebmailUid = Guid.NewGuid(), FullName = "Alice Martin" };

    private OrganizerIdentity Create() => new(_identities.Object, _aliases.Object, _profiles.Object);

    [Fact]
    public async Task Resolve_TakesTheDefaultLiveAlias_WithItsStoredName()
    {
        _aliases.SetupGet(a => a.EnforcesOwnership).Returns(true);
        _aliases.Setup(a => a.GetAddressesAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync(["contact@weesky.net"]);
        _identities.Setup(i => i.GetAsync(_user.WebmailUid, string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "contact@weesky.net", DisplayName = "Alice (contact)", IsDefault = true }]);
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync("Alice");

        Assert.Equal(new OrganizerWrite("contact@weesky.net", "Alice (contact)"), await Create().ResolveAsync(_user, CancellationToken.None));
        _profiles.Verify(p => p.GetDisplayNameAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_CleansTheNameForTheCnParameter()
    {
        _aliases.SetupGet(a => a.EnforcesOwnership).Returns(false);
        _identities.Setup(i => i.GetAsync(_user.WebmailUid, string.Empty, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync("Alice\r\nX-EVIL:1 \"Boss\"");

        Assert.Equal(new OrganizerWrite("alice@weesky.be", "Alice X-EVIL:1 Boss"), await Create().ResolveAsync(_user, CancellationToken.None));
    }

    // A default row the platform does not vouch for — a stale alias, or a free identity on a
    // platform that verifies nothing — falls back to the primary: the only address the service
    // account is always allowed to speak for (décision 8).
    [Fact]
    public async Task Resolve_FallsBackToThePrimary_WhenTheDefaultIsNotOwned()
    {
        _aliases.SetupGet(a => a.EnforcesOwnership).Returns(true);
        _aliases.Setup(a => a.GetAddressesAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _identities.Setup(i => i.GetAsync(_user.WebmailUid, string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "alice@gmail.com", DisplayName = "Alice G", IsDefault = true }]);
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync("Alice");

        Assert.Equal(new OrganizerWrite("alice@weesky.be", "Alice"), await Create().ResolveAsync(_user, CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_NamesThePrimaryAfterTheProfile_ThenTheAccount()
    {
        _aliases.SetupGet(a => a.EnforcesOwnership).Returns(false);
        _identities.Setup(i => i.GetAsync(_user.WebmailUid, string.Empty, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.Equal(new OrganizerWrite("alice@weesky.be", "Alice Martin"), await Create().ResolveAsync(_user, CancellationToken.None));
        _aliases.Verify(a => a.GetAddressesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
