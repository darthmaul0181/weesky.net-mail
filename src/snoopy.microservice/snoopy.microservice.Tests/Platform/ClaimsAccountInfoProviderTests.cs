using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Platform;

public sealed class ClaimsAccountInfoProviderTests
{
    [Fact]
    public async Task GetAccountInfoAsync_SplitsTheEmailIntoUserNameAndMailbox()
    {
        var result = await new ClaimsAccountInfoProvider().GetAccountInfoAsync(
            new User("mick@weesky.be"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("mick", result.Value.UserName);
        Assert.Equal("weesky.be", result.Value.Mailbox);
    }

    /// <summary>Nothing behind the token: no directory row, no numeric id, no admin.</summary>
    [Fact]
    public async Task GetAccountInfoAsync_CarriesNoDirectoryFacts()
    {
        var result = await new ClaimsAccountInfoProvider().GetAccountInfoAsync(
            new User("mick@weesky.be") { FullName = "Mick" }, CancellationToken.None);

        Assert.Equal(0, result.Value.UserId);
        Assert.Null(result.Value.FullName);
        Assert.False(result.Value.IsAdmin);
    }

    /// <summary>
    /// The AccountInfo invariant: Mailbox is the id of one of the Domains rows. The frontend reads
    /// the user's email address out of that row, so a Mailbox matching nothing shows a bare username.
    /// </summary>
    [Fact]
    public async Task GetAccountInfoAsync_CarriesTheMailboxAsASyntheticDomainRow()
    {
        var result = await new ClaimsAccountInfoProvider().GetAccountInfoAsync(
            new User("mick@weesky.be"), CancellationToken.None);

        var domain = Assert.Single(result.Value.Domains);
        Assert.Equal(result.Value.Mailbox, domain.Id);
        Assert.Equal("weesky.be", domain.Name);
    }
}
