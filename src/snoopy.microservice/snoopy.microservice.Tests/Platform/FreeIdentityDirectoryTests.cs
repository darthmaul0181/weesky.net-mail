using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Platform;

public sealed class FreeIdentityDirectoryTests
{
    private readonly FreeIdentityDirectory _directory = new();

    [Fact]
    public void EnforcesOwnership_IsFalse()
    {
        Assert.False(_directory.EnforcesOwnership);
    }

    [Fact]
    public async Task GetAddressesAsync_IsEmpty()
    {
        var addresses = await _directory.GetAddressesAsync(new User("mick@weesky.be"), CancellationToken.None);

        Assert.Empty(addresses);
    }
}
