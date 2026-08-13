using Microsoft.Extensions.Caching.Memory;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

/// <summary>
/// A JWT is valid until it expires, whatever happens to the account meanwhile. The stamp is what
/// closes that: every token carries the value current when it was issued, and rotating the stored
/// one leaves every token already out there unable to match.
/// </summary>
public sealed class SessionGuardTests
{
    private const string Email = "alice@weesky.be";

    private readonly Mock<IAccountInfoProvider> _accounts = new();
    private readonly Mock<IWebmailUserStore> _webmailUsers = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private SessionGuard CreateSut() => new(_accounts.Object, _webmailUsers.Object, _cache);

    private void Account(Guid? storedStamp, bool usable = true)
    {
        _accounts.Setup(a => a.IsUsableAsync(Email, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(usable);
        _webmailUsers.Setup(s => s.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(storedStamp is { } stamp ? new WebmailAccount(Guid.NewGuid(), stamp) : null);
    }

    [Fact]
    public async Task IsCurrent_WhenTheStampMatches_AcceptsTheSession()
    {
        var stamp = Guid.NewGuid();
        Account(stamp);

        Assert.True(await CreateSut().IsCurrentAsync(Email, stamp, CancellationToken.None));
    }

    [Fact]
    public async Task IsCurrent_AfterARotation_RefusesTheTokenThatCarriesTheOldStamp()
    {
        var issued = Guid.NewGuid();
        Account(storedStamp: Guid.NewGuid());

        Assert.False(await CreateSut().IsCurrentAsync(Email, issued, CancellationToken.None));
    }

    [Fact]
    public async Task IsCurrent_WhenTheAccountIsGoneOrDisabled_RefusesWhateverTheStamp()
    {
        var stamp = Guid.NewGuid();
        Account(stamp, usable: false);

        Assert.False(await CreateSut().IsCurrentAsync(Email, stamp, CancellationToken.None));
    }

    // No row means nothing to compare against; trusting the token would make the check optional
    // for exactly the accounts it knows least about.
    [Fact]
    public async Task IsCurrent_WithNoWebmailRow_Refuses()
    {
        Account(storedStamp: null);

        Assert.False(await CreateSut().IsCurrentAsync(Email, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task IsCurrent_ReadsTheAccountOncePerWindowRatherThanPerRequest()
    {
        var stamp = Guid.NewGuid();
        Account(stamp);
        var sut = CreateSut();

        for (var i = 0; i < 5; i++)
            await sut.IsCurrentAsync(Email, stamp, CancellationToken.None);

        _accounts.Verify(a => a.IsUsableAsync(Email, It.IsAny<CancellationToken>()), Times.Once);
        _webmailUsers.Verify(s => s.FindByEmailAsync(Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Trap 4. Without eviction a revocation would keep working for the rest of the cache window;
    // the instance that rotates is the one that must drop its own view of the account.
    [Fact]
    public async Task Forget_MakesARotationTakeEffectAtOnceInsteadOfAtTheEndOfTheWindow()
    {
        var issued = Guid.NewGuid();
        Account(issued);
        var sut = CreateSut();
        Assert.True(await sut.IsCurrentAsync(Email, issued, CancellationToken.None));

        Account(storedStamp: Guid.NewGuid());   // rotated elsewhere
        sut.Forget(Email);

        Assert.False(await sut.IsCurrentAsync(Email, issued, CancellationToken.None));
    }

    [Fact]
    public async Task Forget_IsCaseAndWhitespaceInsensitiveLikeTheLookup()
    {
        var issued = Guid.NewGuid();
        Account(issued);
        var sut = CreateSut();
        await sut.IsCurrentAsync(Email, issued, CancellationToken.None);

        Account(storedStamp: Guid.NewGuid());
        sut.Forget("  Alice@WEESKY.be ");

        Assert.False(await sut.IsCurrentAsync(Email, issued, CancellationToken.None));
    }
}
