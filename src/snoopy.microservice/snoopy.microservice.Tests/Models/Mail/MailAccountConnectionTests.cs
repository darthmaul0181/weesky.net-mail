using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models.Mail;

/// <summary>
/// The staging scope must carry both dimensions: the account id alone collided every user's
/// primary account into one namespace (shared files, shared upload quota).
/// </summary>
public sealed class MailAccountConnectionTests
{
    private static User UserWithUid() => new("alice@weesky.be") { WebmailUid = Guid.NewGuid() };

    [Fact]
    public void StagedScope_TwoUsersOnThePrimary_NeverShareAScope()
    {
        var scopeA = MailAccountConnection.StagedScope(UserWithUid(), MailAccountConnection.Primary);
        var scopeB = MailAccountConnection.StagedScope(UserWithUid(), MailAccountConnection.Primary);

        Assert.NotEqual(scopeA, scopeB);
    }

    [Fact]
    public void StagedScope_PrimaryAndConnectedAccount_DifferForTheSameUser()
    {
        var user = UserWithUid();
        var primary = TestConnections.Primary(user.Email, "pw");
        var connected = primary with { AccountId = Guid.NewGuid().ToString() };

        Assert.NotEqual(primary.StagedScope(user), connected.StagedScope(user));
    }

    [Fact]
    public void StagedScope_InstanceOverload_ComposesFromTheConnectionAccountId()
    {
        var user = UserWithUid();
        var connection = TestConnections.Primary(user.Email, "pw");

        Assert.Equal(
            MailAccountConnection.StagedScope(user, connection.AccountId),
            connection.StagedScope(user));
    }

    [Fact]
    public void StagedScope_ThrowsOnANullUser()
    {
        Assert.Throws<ArgumentNullException>(
            () => MailAccountConnection.StagedScope(null!, MailAccountConnection.Primary));
    }
}
