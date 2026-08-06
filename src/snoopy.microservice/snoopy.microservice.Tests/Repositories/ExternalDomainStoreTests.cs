using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ExternalDomainStoreTests
{
    private static ExternalDomainStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    private static ExternalDomain Domain(string name = "Gmail") =>
        new()
        {
            Name = name,
            ImapHost = "imap.gmail.com",
            ImapPort = 993,
            ImapSecurity = "SslOnConnect",
            SmtpHost = "smtp.gmail.com",
            SmtpPort = 587,
            SmtpSecurity = "StartTls"
        };

    [Fact]
    public async Task CreateAsync_ThenList_ReturnsTheDomain()
    {
        var db = nameof(CreateAsync_ThenList_ReturnsTheDomain);

        var created = await CreateStore(db).CreateAsync(Domain(), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.NotEqual(Guid.Empty, created.Value.Id);
        Assert.NotEqual(default, created.Value.CreationDate);
        var stored = Assert.Single(await CreateStore(db).ListAsync(CancellationToken.None));
        Assert.Equal("Gmail", stored.Name);
        Assert.Equal(993, stored.ImapPort);
    }

    [Fact]
    public async Task ListAsync_SortsByName()
    {
        var db = nameof(ListAsync_SortsByName);
        await CreateStore(db).CreateAsync(Domain("Outlook"), CancellationToken.None);
        await CreateStore(db).CreateAsync(Domain("Gmail"), CancellationToken.None);

        var rows = await CreateStore(db).ListAsync(CancellationToken.None);

        Assert.Equal(["Gmail", "Outlook"], rows.Select(d => d.Name));
    }

    [Fact]
    public async Task CreateAsync_RefusesADuplicateName()
    {
        var db = nameof(CreateAsync_RefusesADuplicateName);
        await CreateStore(db).CreateAsync(Domain(), CancellationToken.None);

        var again = await CreateStore(db).CreateAsync(Domain("  Gmail "), CancellationToken.None);

        Assert.True(again.IsFailure);
        Assert.Equal(ExternalDomainStore.NameTaken, again.Error);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullForAnUnknownId()
    {
        var db = nameof(FindAsync_ReturnsNullForAnUnknownId);

        Assert.Null(await CreateStore(db).FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RewritesEveryField()
    {
        var db = nameof(UpdateAsync_RewritesEveryField);
        var created = await CreateStore(db).CreateAsync(Domain(), CancellationToken.None);

        var edited = Domain("Gmail renamed");
        edited.Id = created.Value.Id;
        edited.SieveHost = "sieve.gmail.com";
        edited.SievePort = 4190;
        var result = await CreateStore(db).UpdateAsync(edited, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await CreateStore(db).FindAsync(created.Value.Id, CancellationToken.None);
        Assert.Equal("Gmail renamed", stored!.Name);
        Assert.Equal("sieve.gmail.com", stored.SieveHost);
        Assert.Equal(4190, stored.SievePort);
    }

    [Fact]
    public async Task UpdateAsync_RewritesTheOAuthProviderColumns()
    {
        var db = nameof(UpdateAsync_RewritesTheOAuthProviderColumns);
        var created = await CreateStore(db).CreateAsync(Domain(), CancellationToken.None);

        var edited = Domain();
        edited.Id = created.Value.Id;
        edited.AuthMode = MailAuthMode.OAuth2;
        edited.OAuthAuthorizationUrl = "https://login.test/authorize";
        edited.OAuthTokenUrl = "https://login.test/token";
        edited.OAuthScopes = "openid email";
        edited.OAuthClientId = "client-1";
        edited.OAuthClientSecret = [1, 2, 3];
        var result = await CreateStore(db).UpdateAsync(edited, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await CreateStore(db).FindAsync(created.Value.Id, CancellationToken.None);
        Assert.Equal(MailAuthMode.OAuth2, stored!.AuthMode);
        Assert.Equal("https://login.test/authorize", stored.OAuthAuthorizationUrl);
        Assert.Equal("https://login.test/token", stored.OAuthTokenUrl);
        Assert.Equal("openid email", stored.OAuthScopes);
        Assert.Equal("client-1", stored.OAuthClientId);
        Assert.Equal(new byte[] { 1, 2, 3 }, stored.OAuthClientSecret);
    }

    [Fact]
    public async Task UpdateAsync_FailsForAnUnknownId()
    {
        var db = nameof(UpdateAsync_FailsForAnUnknownId);
        var orphan = Domain();
        orphan.Id = Guid.NewGuid();

        var result = await CreateStore(db).UpdateAsync(orphan, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ExternalDomainStore.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_RefusesANameAnotherDomainHolds()
    {
        var db = nameof(UpdateAsync_RefusesANameAnotherDomainHolds);
        await CreateStore(db).CreateAsync(Domain("Gmail"), CancellationToken.None);
        var second = await CreateStore(db).CreateAsync(Domain("Outlook"), CancellationToken.None);

        var edited = Domain("Gmail");
        edited.Id = second.Value.Id;
        var result = await CreateStore(db).UpdateAsync(edited, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ExternalDomainStore.NameTaken, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_AcceptsADomainKeepingItsOwnName()
    {
        var db = nameof(UpdateAsync_AcceptsADomainKeepingItsOwnName);
        var created = await CreateStore(db).CreateAsync(Domain(), CancellationToken.None);

        var edited = Domain();
        edited.Id = created.Value.Id;
        edited.ImapHost = "imap2.gmail.com";

        Assert.True((await CreateStore(db).UpdateAsync(edited, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAnUnusedDomain()
    {
        var db = nameof(DeleteAsync_RemovesAnUnusedDomain);
        var created = await CreateStore(db).CreateAsync(Domain(), CancellationToken.None);

        var result = await CreateStore(db).DeleteAsync(created.Value.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await CreateStore(db).ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_FailsForAnUnknownId()
    {
        var db = nameof(DeleteAsync_FailsForAnUnknownId);

        var result = await CreateStore(db).DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ExternalDomainStore.NotFound, result.Error);
    }

    // The FK is ON DELETE RESTRICT: the store must say so rather than let MariaDB throw.
    [Fact]
    public async Task DeleteAsync_RefusesWhileAccountsAreConnected()
    {
        var db = nameof(DeleteAsync_RefusesWhileAccountsAreConnected);
        var created = await CreateStore(db).CreateAsync(Domain(), CancellationToken.None);
        await new ConnectedAccountStore(new PreferencesTestDbContext(db)).CreateAsync(
            new ConnectedAccount
            {
                UserId = Guid.NewGuid(),
                DomainId = created.Value.Id,
                Email = "someone@gmail.com",
                Cipher = [1]
            }, CancellationToken.None);

        var result = await CreateStore(db).DeleteAsync(created.Value.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ExternalDomainStore.InUse, result.Error);
    }
}
