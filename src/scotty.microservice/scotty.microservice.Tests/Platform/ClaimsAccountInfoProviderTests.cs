using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Platform;

public sealed class ClaimsAccountInfoProviderTests
{
    private static ClaimsAccountInfoProvider Provider(params string[] admins) => new(new GenericAdministrators(admins));

    [Fact]
    public async Task GetAccountInfoAsync_IsAdminFollowsTheConfiguredList()
    {
        var provider = Provider("mick@weesky.be");

        Assert.True((await provider.GetAccountInfoAsync(new User("Mick@weesky.be"), CancellationToken.None)).Value.IsAdmin);
        Assert.False((await provider.GetAccountInfoAsync(new User("anne@weesky.be"), CancellationToken.None)).Value.IsAdmin);
    }

    [Fact]
    public async Task GetAccountInfoAsync_SplitsTheEmailIntoUserNameAndMailbox()
    {
        var result = await Provider().GetAccountInfoAsync(
            new User("mick@weesky.be"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("mick", result.Value.UserName);
        Assert.Equal("weesky.be", result.Value.Mailbox);
    }

    /// <summary>Nothing behind the token: no directory row, no numeric id; admin only by the configured list.</summary>
    [Fact]
    public async Task GetAccountInfoAsync_CarriesNoDirectoryFacts()
    {
        var result = await Provider().GetAccountInfoAsync(
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
        var result = await Provider().GetAccountInfoAsync(
            new User("mick@weesky.be"), CancellationToken.None);

        var domain = Assert.Single(result.Value.Domains);
        Assert.Equal(result.Value.Mailbox, domain.Id);
        Assert.Equal("weesky.be", domain.Name);
    }
}
