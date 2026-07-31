using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Real stores over an in-memory database and a real credential store: what these tests pin down
/// is the one code path that turns an account id into hosts and credentials, including its error
/// vocabulary — an outsider's account must be indistinguishable from a missing one.
/// </summary>
public sealed class AccountConnectionResolverTests
{
    private const string MainPassword = "hunter2";

    private readonly User _alice = new("alice@weesky.be") { WebmailUid = Guid.NewGuid() };
    private readonly PreferencesTestDbContext _db = new(Guid.NewGuid().ToString());
    private readonly MailCredentialStore _credentials = new(new EphemeralDataProtectionProvider());
    private readonly WebmailUserStore _users;
    private readonly ConnectedAccountStore _accounts;
    private readonly ExternalDomainStore _domains;
    private readonly byte[] _kek = ConnectedAccountCipher.DeriveKek(MainPassword, ConnectedAccountCipher.NewSalt());

    public AccountConnectionResolverTests()
    {
        _users = new WebmailUserStore(_db);
        _accounts = new ConnectedAccountStore(_db);
        _domains = new ExternalDomainStore(_db);
    }

    private AccountConnectionResolver CreateSut(bool allowCleartext = false)
    {
        var options = TestConnections.HomeOptions();
        options.AllowCleartext = allowCleartext;

        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);

        return new AccountConnectionResolver(
            _credentials, _accounts, _domains, _users, monitor.Object,
            Options.Create(new TokenConstants { ExpiryInMinutes = 2880 }),
            NullLogger<AccountConnectionResolver>.Instance);
    }

    private DefaultHttpContext ContextWithCookie(MailCredentialPayload payload)
    {
        var issued = new DefaultHttpContext();
        _credentials.Store(issued.Response, payload, TimeSpan.FromMinutes(30));

        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"MailCredentials={ExtractCookieValue(issued.Response)}";
        return context;
    }

    private DefaultHttpContext V2Context() => ContextWithCookie(new MailCredentialPayload(MainPassword, _kek));

    private async Task<ConnectedAccount> ConnectAccountAsync(
        string email, string secret, Guid? domainId = null, Guid? ownerId = null, byte[]? kek = null)
    {
        var created = await _accounts.CreateAsync(new ConnectedAccount
        {
            UserId = ownerId ?? _alice.WebmailUid,
            DomainId = domainId,
            Email = email,
            Cipher = ConnectedAccountCipher.Encrypt(kek ?? _kek, secret)
        }, CancellationToken.None);
        return created.Value;
    }

    private async Task<ExternalDomain> CreateDomainAsync(Action<ExternalDomain>? mutate = null)
    {
        var domain = new ExternalDomain
        {
            Name = "Gmail",
            ImapHost = "imap.gmail.test", ImapPort = 993, ImapSecurity = "SslOnConnect",
            SmtpHost = "smtp.gmail.test", SmtpPort = 587, SmtpSecurity = "StartTls",
            SieveHost = "sieve.gmail.test", SievePort = 4190
        };
        mutate?.Invoke(domain);
        return (await _domains.CreateAsync(domain, CancellationToken.None)).Value;
    }

    [Fact]
    public async Task Resolve_WithoutACookie_FailsCredentialsUnavailable()
    {
        var result = await CreateSut().ResolveAsync(
            _alice, new DefaultHttpContext().Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("credentials_unavailable", result.Error);
    }

    [Fact]
    public async Task Resolve_WithoutAnAccountId_DefaultsToThePrimary()
    {
        var result = await CreateSut().ResolveAsync(
            _alice, V2Context().Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestConnections.Primary("alice@weesky.be", MainPassword), result.Value);
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("")]
    public async Task Resolve_HeaderNamingThePrimary_ResolvesThePrimary(string headerValue)
    {
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = headerValue;

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MailAccountConnection.Primary, result.Value.AccountId);
        Assert.True(result.Value.IsHomeServer);
    }

    [Fact]
    public async Task Resolve_HeaderBeatsQuery()
    {
        var account = await ConnectAccountAsync("shared@weesky.be", "sharedpw");
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = MailAccountConnection.Primary;
        context.Request.QueryString = new QueryString($"?account={account.Id}");

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MailAccountConnection.Primary, result.Value.AccountId);
    }

    [Fact]
    public async Task Resolve_QueryAlone_SelectsTheAccount()
    {
        var account = await ConnectAccountAsync("shared@weesky.be", "sharedpw");
        var context = V2Context();
        context.Request.QueryString = new QueryString($"?account={account.Id}");

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.Id.ToString(), result.Value.AccountId);
    }

    [Fact]
    public async Task Resolve_AnUnparseableId_FailsAccountNotFound()
    {
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = "not-a-guid";

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
    }

    [Fact]
    public async Task Resolve_AnUnknownId_FailsAccountNotFound()
    {
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = Guid.NewGuid().ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
    }

    [Fact]
    public async Task Resolve_SomebodyElsesAccount_FailsAccountNotFound()
    {
        var foreign = await ConnectAccountAsync("other@weesky.be", "pw", ownerId: Guid.NewGuid());
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = foreign.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
    }

    [Fact]
    public async Task Resolve_ALocalSharedMailbox_UsesTheHomeEndpointsWithItsOwnCredentials()
    {
        var account = await ConnectAccountAsync("shared@weesky.be", "sharedpw");
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var expected = TestConnections.Primary("shared@weesky.be", "sharedpw") with
        {
            AccountId = account.Id.ToString()
        };
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task Resolve_AnExternalAccount_UsesTheDomainEndpoints()
    {
        var domain = await CreateDomainAsync();
        var account = await ConnectAccountAsync("alice@gmail.test", "gmailpw", domain.Id);
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new MailAccountConnection(
            account.Id.ToString(), IsHomeServer: false,
            "imap.gmail.test", 993, SecureSocketOptions.SslOnConnect,
            "smtp.gmail.test", 587, SecureSocketOptions.StartTls,
            "sieve.gmail.test", 4190,
            "alice@gmail.test", "gmailpw"), result.Value);
    }

    [Fact]
    public async Task Resolve_AMissingDomainRow_FailsAccountNotFound()
    {
        var account = await ConnectAccountAsync("alice@gmail.test", "gmailpw", Guid.NewGuid());
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
    }

    [Theory]
    [InlineData("StartTlsWhenAvailable")]
    [InlineData("Auto")]
    [InlineData("garbage")]
    public async Task Resolve_AnUnusableStoredSecurity_FailsAccountNotFound(string security)
    {
        var domain = await CreateDomainAsync(d => d.ImapSecurity = security);
        var account = await ConnectAccountAsync("alice@gmail.test", "gmailpw", domain.Id);
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
    }

    // The mirror of the IMAP case: with the IMAP value valid, only the SMTP parse can refuse —
    // so the guard's second operand is exercised, not just the first.
    [Fact]
    public async Task Resolve_AnUnusableStoredSmtpSecurity_FailsAccountNotFound()
    {
        var domain = await CreateDomainAsync(d => d.SmtpSecurity = "StartTlsWhenAvailable");
        var account = await ConnectAccountAsync("alice@gmail.test", "gmailpw", domain.Id);
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
    }

    // A row an admin wrote as None would put the mail password on an unencrypted socket: it is
    // refused like any other unusable value, and lands on the same 404 for the same reason.
    [Theory]
    [InlineData("None", "StartTls")]
    [InlineData("SslOnConnect", "None")]
    public async Task Resolve_ACleartextDomain_FailsAccountNotFoundWithoutTheOptIn(string imap, string smtp)
    {
        var domain = await CreateDomainAsync(d => (d.ImapSecurity, d.SmtpSecurity) = (imap, smtp));
        var account = await ConnectAccountAsync("alice@gmail.test", "gmailpw", domain.Id);
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
    }

    [Fact]
    public async Task Resolve_ACleartextDomain_IsUsableWhenAllowCleartextIsOn()
    {
        var domain = await CreateDomainAsync(d => (d.ImapSecurity, d.SmtpSecurity) = ("None", "None"));
        var account = await ConnectAccountAsync("alice@gmail.test", "gmailpw", domain.Id);
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut(allowCleartext: true)
            .ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SecureSocketOptions.None, result.Value.ImapSecurity);
        Assert.Equal(SecureSocketOptions.None, result.Value.SmtpSecurity);
    }

    [Theory]
    [InlineData("StartTls")]
    [InlineData("SslOnConnect")]
    public async Task Resolve_AnEncryptedDomain_IsUnaffectedByTheOptIn(string security)
    {
        var domain = await CreateDomainAsync(d => (d.ImapSecurity, d.SmtpSecurity) = (security, security));
        var account = await ConnectAccountAsync("alice@gmail.test", "gmailpw", domain.Id);
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Enum.Parse<SecureSocketOptions>(security), result.Value.ImapSecurity);
    }

    [Fact]
    public async Task Resolve_ACipherTheKekNoLongerOpens_FailsCredentialsInvalid()
    {
        // Ciphered under another KEK: the main password changed outside the app.
        var account = await ConnectAccountAsync(
            "shared@weesky.be", "sharedpw", kek: ConnectedAccountCipher.DeriveKek("old", ConnectedAccountCipher.NewSalt()));
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public async Task Resolve_AV1Cookie_DerivesTheKekAndReissuesAV2Cookie()
    {
        // The account's cipher hangs off the persisted salt, the one the resolver must reuse.
        await _users.RegisterLoginAsync("alice@weesky.be", CancellationToken.None);
        var salt = await _users.GetOrCreateKdfSaltAsync("alice@weesky.be", CancellationToken.None);
        var kek = ConnectedAccountCipher.DeriveKek(MainPassword, salt);
        var account = await ConnectAccountAsync("shared@weesky.be", "sharedpw", kek: kek);

        var context = ContextWithCookie(new MailCredentialPayload(MainPassword, null));
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("sharedpw", result.Value.Password);

        // The response now carries the upgraded cookie, KEK included.
        var reread = new DefaultHttpContext();
        reread.Request.Headers.Cookie = $"MailCredentials={ExtractCookieValue(context.Response)}";
        var upgraded = _credentials.Retrieve(reread.Request);
        Assert.True(upgraded.IsSuccess);
        Assert.Equal(MainPassword, upgraded.Value.Password);
        Assert.Equal<byte[]>(kek, upgraded.Value.Kek!);
    }

    [Fact]
    public async Task Resolve_ThePrimaryWithAV1Cookie_NeitherDerivesNorReissues()
    {
        var context = ContextWithCookie(new MailCredentialPayload(MainPassword, null));

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.Response.Headers["Set-Cookie"].ToArray());
    }

    private static string ExtractCookieValue(HttpResponse response)
    {
        var header = string.Join(";", response.Headers["Set-Cookie"].ToArray());
        const string name = "MailCredentials=";
        var start = header.IndexOf(name, StringComparison.Ordinal) + name.Length;
        var end = header.IndexOf(';', start);
        return end < 0 ? header[start..] : header[start..end];
    }
}
