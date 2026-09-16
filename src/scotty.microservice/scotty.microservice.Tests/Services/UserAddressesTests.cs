using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Platform;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class UserAddressesTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private readonly User _user = new("alice@weesky.be") { WebmailUid = WebmailUid };
    private readonly Mock<IAccountInfoProvider> _accounts = new();
    private readonly Mock<ISendingIdentityStore> _identities = new();

    private UserAddresses Create()
    {
        _accounts.Setup(a => a.GetAccountInfoAsync(_user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AccountInfo
            {
                Domains = [new Domain { Name = "weesky.be" }, new Domain { Name = "Weesky.net" }],
            }));
        _identities.Setup(i => i.GetAsync(WebmailUid, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "Alice.Pro@weesky.be" }]);
        _identities.Setup(i => i.GetAllAsync(WebmailUid, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "Alice.Pro@weesky.be" }, new SendingIdentity { Address = "me@gmail.com" }]);
        return new UserAddresses(_accounts.Object, _identities.Object, NullLogger<UserAddresses>.Instance);
    }

    [Fact]
    public async Task Primary_IsTheAddressItsOtherDomainsAndItsOwnIdentities_LowerCased()
    {
        var conn = TestConnections.Primary("alice@weesky.be", "pw");

        var addresses = await Create().ForAccountAsync(_user, conn, CancellationToken.None);

        Assert.Equal(["alice@weesky.be", "alice@weesky.net", "alice.pro@weesky.be"], addresses);
    }

    [Fact]
    public async Task Connected_IsItsLoginAndItsOwnIdentities()
    {
        var id = Guid.NewGuid().ToString();
        var conn = TestConnections.Connected(id, "Me@Gmail.com", "pw");
        _identities.Setup(i => i.GetAsync(WebmailUid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "alias@gmail.com" }]);

        var addresses = await Create().ForAccountAsync(_user, conn, CancellationToken.None);

        Assert.Equal(["me@gmail.com", "alias@gmail.com"], addresses);
        _accounts.Verify(a => a.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Principal_KeepsWhat5cPublished_EveryIdentityOfEveryAccount()
    {
        var addresses = await Create().ForPrincipalAsync(_user, CancellationToken.None);

        Assert.Equal(["alice@weesky.be", "alice@weesky.net", "alice.pro@weesky.be", "me@gmail.com"], addresses);
    }

    // Organizer side: the identity of a connected account (Gmail) is somebody the user may invite, never the organizer.
    [Fact]
    public async Task ForPrimary_IsTheHomeListAndThePrimaryAccountsIdentities_WithoutAConnectedAccounts()
    {
        var addresses = await Create().ForPrimaryAsync(_user, CancellationToken.None);

        Assert.Equal(["alice@weesky.be", "alice@weesky.net", "alice.pro@weesky.be"], addresses);
    }

    // The event's detail reads both lists, the save and its hook the organizer's twice: the platform is asked once per request.
    [Fact]
    public async Task TheHomeList_IsReadOnce_ForEveryReadingOfTheSameRequest()
    {
        var sut = Create();

        await sut.ForPrincipalAsync(_user, CancellationToken.None);
        await sut.ForPrimaryAsync(_user, CancellationToken.None);
        await sut.ForAccountAsync(_user, TestConnections.Primary("alice@weesky.be", "pw"), CancellationToken.None);

        _accounts.Verify(a => a.GetAccountInfoAsync(_user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Primary_WhenThePlatformCannotAnswer_IsTheAddressAlone_AndWarns()
    {
        var sut = Create();
        _accounts.Setup(a => a.GetAccountInfoAsync(_user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AccountInfo>("down"));

        var addresses = await sut.ForAccountAsync(_user, TestConnections.Primary("alice@weesky.be", "pw"), CancellationToken.None);

        Assert.Equal(["alice@weesky.be", "alice.pro@weesky.be"], addresses);
    }
}
