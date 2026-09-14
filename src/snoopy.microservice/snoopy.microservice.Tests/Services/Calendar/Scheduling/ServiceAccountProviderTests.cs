using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Scheduling;

public sealed class ServiceAccountProviderTests : IDisposable
{
    private const string Password = "smtp-p4ss";

    private readonly string _database = Guid.NewGuid().ToString("N");
    private readonly ServiceAccountSecretProtector _protector =
        new(DataProtectionProvider.Create(nameof(ServiceAccountProviderTests)));
    private readonly Mock<ILogger<ServiceAccountProvider>> _logger = new();
    private readonly ServiceProvider _services;
    private int _stores;

    public ServiceAccountProviderTests()
    {
        _services = new ServiceCollection()
            .AddScoped<PreferencesDbContext>(_ => new PreferencesTestDbContext(_database))
            .AddScoped<ISchedulingAccountStore>(sp =>
            {
                Interlocked.Increment(ref _stores);
                return new SchedulingAccountStore(sp.GetRequiredService<PreferencesDbContext>());
            })
            .BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private ServiceAccountProvider Create() =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _protector, _logger.Object);

    private Task SaveAsync(string host = "smtp.weesky.be", byte[]? cipher = null) =>
        new SchedulingAccountStore(new PreferencesTestDbContext(_database)).SaveAsync(
            host, 465, "SslOnConnect", "noreply-agenda@weesky.net", cipher ?? _protector.Protect(Password), CancellationToken.None);

    private Task DeleteAsync() =>
        new SchedulingAccountStore(new PreferencesTestDbContext(_database)).DeleteAsync(CancellationToken.None);

    [Fact]
    public void BeforeAnyLoad_WhetherItIsConfigured_IsUnknown()
    {
        Assert.Null(Create().IsConfigured);
    }

    [Fact]
    public async Task WithNoRow_ThereIsNoAccount()
    {
        var provider = Create();

        Assert.Null(await provider.GetAsync(CancellationToken.None));
        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task TheStoredRow_OpensWithItsPassword()
    {
        await SaveAsync();
        var provider = Create();

        var account = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(new ServiceSmtpAccount("smtp.weesky.be", 465, SecureSocketOptions.SslOnConnect, "noreply-agenda@weesky.net", Password), account);
        Assert.True(provider.IsConfigured);
        Assert.DoesNotContain(Password, account!.ToString());
    }

    [Fact]
    public async Task InSteadyState_TheDatabaseIsNotReadAgain()
    {
        await SaveAsync();
        var provider = Create();

        await provider.GetAsync(CancellationToken.None);
        await DeleteAsync();
        var again = await provider.GetAsync(CancellationToken.None);

        Assert.NotNull(again);
        Assert.Equal(1, _stores);
    }

    [Fact]
    public async Task AnUndecryptablePassword_IsNotConfigured_WithOneWarningAndNoSecret()
    {
        await SaveAsync(cipher: [1, 2, 3, 4]);
        var provider = Create();

        Assert.Null(await provider.GetAsync(CancellationToken.None));
        Assert.Null(await provider.GetAsync(CancellationToken.None));

        Assert.False(provider.IsConfigured);
        _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        _logger.VerifyNoLoggedValueContains(Password);
    }

    [Fact]
    public async Task Invalidating_PicksUpASave()
    {
        var provider = Create();
        await provider.GetAsync(CancellationToken.None);

        await SaveAsync(host: "mail.weesky.be");
        await provider.InvalidateAsync(CancellationToken.None);

        Assert.True(provider.IsConfigured);
        Assert.Equal("mail.weesky.be", (await provider.GetAsync(CancellationToken.None))!.Host);
    }

    [Fact]
    public async Task Invalidating_PicksUpADelete()
    {
        await SaveAsync();
        var provider = Create();
        await provider.GetAsync(CancellationToken.None);

        await DeleteAsync();
        await provider.InvalidateAsync(CancellationToken.None);

        Assert.False(provider.IsConfigured);
        Assert.Null(await provider.GetAsync(CancellationToken.None));
    }

    // A load that read the row before a save must not be kept once the save's invalidation has landed.
    [Fact]
    public async Task ALoadOverlappingAnInvalidation_IsNotKept()
    {
        await SaveAsync(host: "before.weesky.be");
        var readDone = new TaskCompletionSource();
        var resume = new TaskCompletionSource();
        var calls = 0;
        var store = new Mock<ISchedulingAccountStore>();
        store.Setup(s => s.FindAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                var row = await new SchedulingAccountStore(new PreferencesTestDbContext(_database)).FindAsync(ct);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    readDone.TrySetResult();
                    await resume.Task;
                }
                return row;
            });
        await using var services = new ServiceCollection().AddScoped(_ => store.Object).BuildServiceProvider();
        var provider = new ServiceAccountProvider(services.GetRequiredService<IServiceScopeFactory>(), _protector, _logger.Object);

        var stale = provider.GetAsync(CancellationToken.None);
        await readDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SaveAsync(host: "after.weesky.be");
        var invalidation = provider.InvalidateAsync(CancellationToken.None);
        resume.TrySetResult();

        Assert.Equal("before.weesky.be", (await stale.WaitAsync(TimeSpan.FromSeconds(5)))!.Host);
        await invalidation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("after.weesky.be", (await provider.GetAsync(CancellationToken.None))!.Host);
    }

    [Fact]
    public async Task AReloadThatFails_LeavesTheStateUnknown_AndDoesNotThrow()
    {
        await SaveAsync();
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(s => s.CreateScope()).Throws(new InvalidOperationException("database down"));
        var provider = new ServiceAccountProvider(scopes.Object, _protector, _logger.Object);

        await provider.InvalidateAsync(CancellationToken.None);

        Assert.Null(provider.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync(CancellationToken.None));
    }
}
