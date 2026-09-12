using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

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
