using CSharpFunctionalExtensions;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.CardDav;
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
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly PreferencesTestDbContext _db;
    private readonly MailCredentialStore _credentials = new(new EphemeralDataProtectionProvider());
    private readonly WebmailUserStore _users;
    private readonly ConnectedAccountStore _accounts;
    private readonly ExternalDomainStore _domains;
    private readonly byte[] _kek = ConnectedAccountCipher.DeriveKek(MainPassword, ConnectedAccountCipher.NewSalt());
    private readonly Mock<IOAuthTokenService> _oauth = new();
    private readonly RequestIdentity _identity = new();

    /// <summary>
    /// A second context over the same database, as production has one per request. The binding
    /// upgrade attaches the row it was handed, which the arrange context would already be tracking.
    /// </summary>
    private readonly ConnectedAccountStore _arrangedAccounts;

    public AccountConnectionResolverTests()
    {
        _db = new PreferencesTestDbContext(_databaseName);
        _users = new WebmailUserStore(_db, new DavAuthenticationCache(TimeProvider.System));
        _accounts = new ConnectedAccountStore(_db);
        _domains = new ExternalDomainStore(_db);
        _arrangedAccounts = new ConnectedAccountStore(new PreferencesTestDbContext(_databaseName));
    }

    private AccountConnectionResolver CreateSut(bool allowCleartext = false)
    {
        var options = TestConnections.HomeOptions();
        options.AllowCleartext = allowCleartext;

        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);

        return new AccountConnectionResolver(
            _credentials, _accounts, _domains, _users, _oauth.Object, monitor.Object,
            Options.Create(new TokenConstants { ExpiryInMinutes = 2880 }),
            _identity, NullLogger<AccountConnectionResolver>.Instance);
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
        string email, string secret, Guid? domainId = null, Guid? ownerId = null, byte[]? kek = null,
        MailAuthMode authMode = MailAuthMode.Password)
    {
        // Id first, like the controller: the cipher is bound to the row it will live on.
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(),
            UserId = ownerId ?? _alice.WebmailUid,
            DomainId = domainId,
            Email = email,
            AuthMode = authMode
        };
        row.Cipher = ConnectedAccountCipher.Encrypt(
            kek ?? _kek, secret, ConnectedAccountCipher.Context(row));

        var created = await _accounts.CreateAsync(row, CancellationToken.None);
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

    /// <summary>
    /// A row as it exists today: nonce, tag, ciphertext, no version byte and no associated data.
    /// Written straight through the store, since the cipher is exactly what is being aged.
    /// </summary>
    private async Task<ConnectedAccount> ConnectUnboundAccountAsync(string email, string secret)
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes(secret);
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(
            ConnectedAccountCipher.NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[ConnectedAccountCipher.TagLength];

        using (var aes = new System.Security.Cryptography.AesGcm(_kek, ConnectedAccountCipher.TagLength))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, nonce.Length);
        ciphertext.CopyTo(blob, nonce.Length + tag.Length);

        var created = await _arrangedAccounts.CreateAsync(new ConnectedAccount
        {
            Id = Guid.NewGuid(),
            UserId = _alice.WebmailUid,
            Email = email,
            Cipher = blob
        }, CancellationToken.None);
        return created.Value;
    }

    // The migration, and the reason the binding is worth anything on an existing deployment: no
    // user is asked for a provider password again, so an old row has to bind itself the first
    // time it is opened. Without this, only accounts created after the deploy are protected.
    [Fact]
    public async Task Resolve_BindsAPreBindingCipherOnFirstUse()
    {
        var account = await ConnectUnboundAccountAsync("legacy@weesky.be", "provider-pw");
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PasswordCredential("provider-pw"), result.Value.Credential);

        var stored = await _arrangedAccounts.FindAsync(_alice.WebmailUid, account.Id, CancellationToken.None);
        ConnectedAccountCipher.Decrypt(
            _kek, stored!.Cipher, ConnectedAccountCipher.Context(stored), out var bound);
        Assert.True(bound);
    }

    // Binding must not change what the row means: the same secret comes back on the next request,
    // now through the bound path.
    [Fact]
    public async Task Resolve_StillOpensTheAccountAfterItHasBeenBound()
    {
        var account = await ConnectUnboundAccountAsync("legacy@weesky.be", "provider-pw");
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();
        await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        var again = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Equal(new PasswordCredential("provider-pw"), again.Value.Credential);
    }

    [Fact]
    public async Task Resolve_WithoutACookie_FailsCredentialsUnavailable()
    {
        var result = await CreateSut().ResolveAsync(
            _alice, new DefaultHttpContext().Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("credentials_unavailable", result.Error);
    }

    // The pool indexes by user, and this is the only place on the mail path that holds the user.
    [Fact]
    public async Task Resolve_RecordsTheUserForThePool()
    {
        await CreateSut().ResolveAsync(_alice, V2Context().Request, CancellationToken.None);

        Assert.Equal(_alice.WebmailUid, _identity.UserUid);
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
            "alice@gmail.test", new PasswordCredential("gmailpw")), result.Value);
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
        Assert.Equal(new PasswordCredential("sharedpw"), result.Value.Credential);

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

    /// <summary>An OAuth provider with all five columns filled, as the admin screen will write it.</summary>
    private static void MakeOAuth(ExternalDomain domain)
    {
        domain.AuthMode = MailAuthMode.OAuth2;
        domain.OAuthAuthorizationUrl = "https://provider.test/authorize";
        domain.OAuthTokenUrl = "https://provider.test/token";
        domain.OAuthScopes = "offline_access";
        domain.OAuthClientId = "client-id";
        domain.OAuthClientSecret = [1, 2, 3];
    }

    private async Task<(ConnectedAccount Account, DefaultHttpContext Context)> ConnectOAuthAccountAsync(
        Action<ExternalDomain>? mutate = null)
    {
        var domain = await CreateDomainAsync(d => { MakeOAuth(d); mutate?.Invoke(d); });
        var account = await ConnectAccountAsync(
            "alice@outlook.test", "rt-1", domain.Id, authMode: MailAuthMode.OAuth2);
        var context = V2Context();
        context.Request.Headers[IAccountConnectionResolver.HeaderName] = account.Id.ToString();
        return (account, context);
    }

    [Fact]
    public async Task ResolveAsync_OfAnOAuthAccount_CarriesTheAccessToken()
    {
        var (account, context) = await ConnectOAuthAccountAsync();
        _oauth.Setup(o => o.GetAccessTokenAsync(
                It.Is<ConnectedAccount>(r => r.Id == account.Id), It.IsAny<OAuthProviderConfig>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("at-1"));

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new OAuthCredential("at-1"), result.Value.Credential);
        Assert.False(result.Value.IsHomeServer);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheProviderRefusesTheRefreshToken_Answers409()
    {
        var (_, context) = await ConnectOAuthAccountAsync();
        _oauth.Setup(o => o.GetAccessTokenAsync(
                It.IsAny<ConnectedAccount>(), It.IsAny<OAuthProviderConfig>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string>(ConnectedAccountErrors.CredentialsInvalid));

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheProviderIsUnreachable_AnswersProviderUnavailable()
    {
        var (_, context) = await ConnectOAuthAccountAsync();
        _oauth.Setup(o => o.GetAccessTokenAsync(
                It.IsAny<ConnectedAccount>(), It.IsAny<OAuthProviderConfig>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string>(ConnectedAccountErrors.ProviderUnavailable));

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.ProviderUnavailable, result.Error);
    }

    // Administrator error, not user error: the same 404 an unusable security value answers, and
    // the token service is never asked to work with a half-described provider.
    [Fact]
    public async Task ResolveAsync_AnIncompleteOAuthDomain_FailsAccountNotFoundWithoutExchanging()
    {
        var (_, context) = await ConnectOAuthAccountAsync(d => d.OAuthTokenUrl = null);

        var result = await CreateSut().ResolveAsync(_alice, context.Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, result.Error);
        _oauth.Verify(o => o.GetAccessTokenAsync(
            It.IsAny<ConnectedAccount>(), It.IsAny<OAuthProviderConfig>(),
            It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
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
