using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.ConnectedAccounts;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// The consent handshake over the same fixture shape as <see cref="ConnectedAccountsControllerTests"/>:
/// real stores over an in-memory database, a real credential store, and the two OAuth boundaries
/// mocked — the handshake store because the claims are about what the actions hand it, and the
/// token service because no test may reach a provider.
/// </summary>
public sealed class ConnectedAccountsOAuthTests
{
    private const string MainPassword = "hunter2";
    private const string State = "st-1";
    private const string WebmailBaseUrl = "https://mail.test";
    private const string RedirectUri = "https://api.test/api/ConnectedAccounts/OAuth/Callback";

    private static readonly Guid Uid = Guid.NewGuid();

    private readonly MailCredentialStore _credentials = new(new EphemeralDataProtectionProvider());
    private readonly Mock<IImapConnectionFactory> _imap = new();
    private readonly Mock<IOAuthHandshakeStore> _handshakes = new();
    private readonly Mock<IOAuthTokenService> _oauth = new();
    private readonly ConnectedAccountStore _accounts;
    private readonly ExternalDomainStore _domains;
    private readonly SendingIdentityStore _identities;
    private readonly WebmailUserStore _users;
    private readonly ConnectedAccountStore _arrangedAccounts;

    private readonly byte[] _kek =
        ConnectedAccountCipher.DeriveKek(MainPassword, ConnectedAccountCipher.NewSalt());

    public ConnectedAccountsOAuthTests()
    {
        var databaseName = Guid.NewGuid().ToString();
        var db = new PreferencesTestDbContext(databaseName);
        var arranged = new PreferencesTestDbContext(databaseName);

        _accounts = new ConnectedAccountStore(db);
        _domains = new ExternalDomainStore(db);
        _identities = new SendingIdentityStore(db);
        _users = new WebmailUserStore(db);
        _arrangedAccounts = new ConnectedAccountStore(arranged);
    }

    private ConnectedAccountsController CreateController(
        bool withCookie = true, ILogger<ConnectedAccountsController>? logger = null)
    {
        var mailOptions = TestConnections.HomeOptions();
        mailOptions.WebmailBaseUrl = WebmailBaseUrl;
        mailOptions.OAuthRedirectUri = RedirectUri;
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(mailOptions);

        var controller = new ConnectedAccountsController(
            _accounts, _domains, _identities, _credentials, _users, _imap.Object,
            _handshakes.Object, _oauth.Object, monitor.Object,
            logger ?? NullLogger<ConnectedAccountsController>.Instance)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", Uid)
        };

        if (withCookie)
            controller.Request.Headers.Cookie = $"MailCredentials={IssueCookie()}";
        return controller;
    }

    private string IssueCookie()
    {
        var issued = new DefaultHttpContext();
        _credentials.Store(
            issued.Response, new MailCredentialPayload(MainPassword, _kek), TimeSpan.FromMinutes(30));

        var header = string.Join(";", issued.Response.Headers["Set-Cookie"].ToArray());
        const string name = "MailCredentials=";
        var start = header.IndexOf(name, StringComparison.Ordinal) + name.Length;
        var end = header.IndexOf(';', start);
        return end < 0 ? header[start..] : header[start..end];
    }

    private async Task<ExternalDomain> CreateOAuthDomainAsync(Action<ExternalDomain>? mutate = null)
    {
        var domain = new ExternalDomain
        {
            Name = "Outlook",
            ImapHost = "outlook.office365.test", ImapPort = 993, ImapSecurity = "SslOnConnect",
            SmtpHost = "smtp.office365.test", SmtpPort = 587, SmtpSecurity = "StartTls",
            AuthMode = MailAuthMode.OAuth2,
            OAuthAuthorizationUrl = "https://login.provider.test/authorize",
            OAuthTokenUrl = "https://login.provider.test/token",
            OAuthScopes = "offline_access openid email profile",
            OAuthClientId = "client-123",
            OAuthClientSecret = [1, 2, 3]
        };
        mutate?.Invoke(domain);
        return (await _domains.CreateAsync(domain, CancellationToken.None)).Value;
    }

    private async Task<ExternalDomain> CreatePasswordDomainAsync() =>
        (await _domains.CreateAsync(new ExternalDomain
        {
            Name = "Gmail",
            ImapHost = "imap.gmail.test", ImapPort = 993, ImapSecurity = "SslOnConnect",
            SmtpHost = "smtp.gmail.test", SmtpPort = 587, SmtpSecurity = "StartTls"
        }, CancellationToken.None)).Value;

    private async Task<ConnectedAccount> ConnectedAsync(
        string email, Guid? domainId, MailAuthMode authMode, string secret = "rt-old",
        Guid? ownerId = null)
    {
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(),
            UserId = ownerId ?? Uid,
            DomainId = domainId,
            Email = email,
            AuthMode = authMode
        };
        row.Cipher = ConnectedAccountCipher.Encrypt(
            _kek, secret, ConnectedAccountCipher.Context(row));
        return (await _arrangedAccounts.CreateAsync(row, CancellationToken.None)).Value;
    }

    private static OAuthHandshake Handshake(
        Guid domainId, Guid? accountId = null, OAuthTokenResponse? tokens = null, string? email = null) =>
        new(State, Uid, domainId, accountId, "ver", "chal", tokens, email);

    private static OAuthTokenResponse Tokens(string? idToken = null, string? refreshToken = "rt") =>
        new("at", refreshToken, 3600, idToken);

    /// <summary>An unsigned id_token, exactly what the handler parses without validating.</summary>
    private static string IdToken(object claims) =>
        new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(claims));

    private void AssertNeverOpened() => _imap.Verify(
        f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);

    private void AssertNeverExchanged() => _oauth.Verify(
        o => o.ExchangeCodeAsync(
            It.IsAny<OAuthProviderConfig>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);

    private void AssertNeverAttached() => _handshakes.Verify(
        h => h.Attach(It.IsAny<string>(), It.IsAny<OAuthTokenResponse>(), It.IsAny<string>()),
        Times.Never);

    // ---- Start ----

    [Fact]
    public async Task OAuthStart_OnAPasswordDomain_AnswersBadRequest()
    {
        var domain = await CreatePasswordDomainAsync();

        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(domain.Id, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _handshakes.Verify(
            h => h.Start(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
    }

    // The user clicked a provider button this server rendered off authMode: the refusal must not
    // claim the domain signs in with a password, and the admin error must reach the log.
    [Fact]
    public async Task OAuthStart_OnAnIncompleteOAuthDomain_LogsTheAdminErrorAndStaysGeneric()
    {
        var domain = await CreateOAuthDomainAsync(d => d.OAuthTokenUrl = null);
        var logger = new Mock<ILogger<ConnectedAccountsController>>();

        var result = await CreateController(logger: logger.Object).OAuthStart(
            new OAuthStartRequest(domain.Id, null), CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "This provider sign-in is not available right now. Contact your administrator.",
            Assert.IsType<ResultEnveloppe>(refused.Value).Message);
        logger.Verify(
            l => l.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _handshakes.Verify(
            h => h.Start(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task OAuthStart_OnAPasswordDomain_LogsNothing()
    {
        var domain = await CreatePasswordDomainAsync();
        var logger = new Mock<ILogger<ConnectedAccountsController>>();

        var result = await CreateController(logger: logger.Object).OAuthStart(
            new OAuthStartRequest(domain.Id, null), CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(
            "This server does not sign in with a provider account",
            Assert.IsType<ResultEnveloppe>(refused.Value).Message);
        logger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void ToString_OfTheHandshakeRequestDtos_NeverPrintsTheState()
    {
        Assert.DoesNotContain(State, new OAuthCompleteRequest(State).ToString());
        Assert.StartsWith("OAuthStartRequest", new OAuthStartRequest(Guid.NewGuid(), null).ToString());
    }

    [Fact]
    public async Task OAuthStart_OnAnUnknownDomain_AnswersBadRequest()
    {
        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(Guid.NewGuid(), null), CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Unknown domain", Assert.IsType<ResultEnveloppe>(refused.Value).Message);
    }

    [Fact]
    public async Task OAuthStart_WithNeitherId_AnswersBadRequest()
    {
        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(null, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OAuthStart_WithBothIds_AnswersBadRequest()
    {
        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OAuthStart_ForAForeignAccount_AnswersNotFound()
    {
        var domain = await CreateOAuthDomainAsync();
        var foreign = await ConnectedAsync(
            "other@outlook.test", domain.Id, MailAuthMode.OAuth2, ownerId: Guid.NewGuid());

        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(null, foreign.Id), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task OAuthStart_OnACompleteOAuthDomain_AnswersTheAuthorizationUrl()
    {
        var domain = await CreateOAuthDomainAsync();
        _handshakes.Setup(h => h.Start(Uid, domain.Id, null)).Returns(Handshake(domain.Id));

        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(domain.Id, null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<OAuthStartResponse>(ok.Value);
        Assert.Equal(State, response.State);
        Assert.StartsWith("https://login.provider.test/authorize?", response.AuthorizationUrl);
        Assert.Contains("client_id=client-123", response.AuthorizationUrl);
        Assert.Contains("response_type=code", response.AuthorizationUrl);
        Assert.Contains("code_challenge=chal", response.AuthorizationUrl);
        Assert.Contains("code_challenge_method=S256", response.AuthorizationUrl);
        Assert.Contains($"state={State}", response.AuthorizationUrl);
        Assert.Contains("access_type=offline", response.AuthorizationUrl);
        // Deliberate, not vestigial: Microsoft honours prompt=consent (full dialog per consent),
        // and it is what guarantees a reconnect a fresh grant. See AuthorizationUrl's comment.
        Assert.Contains("prompt=consent", response.AuthorizationUrl);
        Assert.Contains(
            Uri.EscapeDataString("offline_access openid email profile"), response.AuthorizationUrl);
        Assert.Contains(Uri.EscapeDataString(RedirectUri), response.AuthorizationUrl);
    }

    // The mirror of the UpdatePassword guard: a Password row must not come back holding a token
    // its own auth_mode would replay as a password.
    [Fact]
    public async Task OAuthStart_ReconnectingAPasswordRow_AnswersBadRequest()
    {
        var domain = await CreateOAuthDomainAsync();
        var row = await ConnectedAsync("legacy@outlook.test", domain.Id, MailAuthMode.Password);

        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(null, row.Id), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _handshakes.Verify(
            h => h.Start(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task OAuthStart_ReconnectingAnOAuthRow_StartsAHandshakeCarryingTheAccount()
    {
        var domain = await CreateOAuthDomainAsync();
        var row = await ConnectedAsync("alice@outlook.test", domain.Id, MailAuthMode.OAuth2);
        _handshakes.Setup(h => h.Start(Uid, domain.Id, row.Id)).Returns(Handshake(domain.Id, row.Id));

        var result = await CreateController().OAuthStart(
            new OAuthStartRequest(null, row.Id), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        _handshakes.Verify(h => h.Start(Uid, domain.Id, row.Id), Times.Once);
    }

    // ---- Callback ----

    [Fact]
    public async Task OAuthCallback_WithAnUnknownState_RedirectsWithAnErrorAndNeverExchanges()
    {
        var redirect = Assert.IsType<RedirectResult>(await CreateController(withCookie: false)
            .OAuthCallback("code-abc", "unknown", error: null, CancellationToken.None));

        // Exact, not Contains: Packet 4 reads this URL, and the SPA route table's catch-all
        // means a drifted path silently drops the consent instead of failing loudly.
        Assert.Equal($"{WebmailBaseUrl}/settings/accounts?oauthError=1", redirect.Url);
        AssertNeverExchanged();
    }

    [Fact]
    public async Task OAuthCallback_WhenTheProviderReportsAnError_RedirectsWithoutExchanging()
    {
        var redirect = Assert.IsType<RedirectResult>(await CreateController(withCookie: false)
            .OAuthCallback(code: null, State, "access_denied", CancellationToken.None));

        Assert.Contains("oauthError=1", redirect.Url);
        AssertNeverExchanged();
        AssertNeverAttached();
    }

    [Fact]
    public async Task OAuthCallback_OnAGoodState_ExchangesAttachesAndCarriesTheState()
    {
        var domain = await CreateOAuthDomainAsync();
        var tokens = Tokens(IdToken(new { email = "Alice@Outlook.test" }));
        _handshakes.Setup(h => h.Find(State)).Returns(Handshake(domain.Id));
        _handshakes.Setup(h => h.Attach(State, tokens, "alice@outlook.test")).Returns(true);
        _oauth.Setup(o => o.ExchangeCodeAsync(
                It.IsAny<OAuthProviderConfig>(), "code-abc", "ver", RedirectUri,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(tokens));

        var redirect = Assert.IsType<RedirectResult>(await CreateController(withCookie: false)
            .OAuthCallback("code-abc", State, error: null, CancellationToken.None));

        // The exact URL Packet 4 resumes from: the state travels back, the code never does.
        Assert.Equal($"{WebmailBaseUrl}/settings/accounts?oauthState={State}", redirect.Url);
        Assert.DoesNotContain("code-abc", redirect.Url);
        _oauth.Verify(o => o.ExchangeCodeAsync(
            It.Is<OAuthProviderConfig>(p => p.ClientId == "client-123"), "code-abc", "ver",
            RedirectUri, It.IsAny<CancellationToken>()), Times.Once);
        _handshakes.Verify(h => h.Attach(State, tokens, "alice@outlook.test"), Times.Once);
    }

    [Fact]
    public async Task OAuthCallback_WithAMalformedIdToken_RedirectsWithAnErrorAndAttachesNothing()
    {
        var domain = await CreateOAuthDomainAsync();
        _handshakes.Setup(h => h.Find(State)).Returns(Handshake(domain.Id));
        _oauth.Setup(o => o.ExchangeCodeAsync(
                It.IsAny<OAuthProviderConfig>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Tokens("not-a-jwt")));

        var redirect = Assert.IsType<RedirectResult>(await CreateController(withCookie: false)
            .OAuthCallback("code-abc", State, error: null, CancellationToken.None));

        Assert.Contains("oauthError=1", redirect.Url);
        AssertNeverAttached();
    }

    // Personal Microsoft accounts routinely carry the address in preferred_username only.
    [Fact]
    public async Task OAuthCallback_ReadsPreferredUsernameWhenEmailIsAbsent()
    {
        var domain = await CreateOAuthDomainAsync();
        var tokens = Tokens(IdToken(new { preferred_username = "bob@outlook.test" }));
        _handshakes.Setup(h => h.Find(State)).Returns(Handshake(domain.Id));
        _handshakes.Setup(h => h.Attach(State, tokens, "bob@outlook.test")).Returns(true);
        _oauth.Setup(o => o.ExchangeCodeAsync(
                It.IsAny<OAuthProviderConfig>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(tokens));

        var redirect = Assert.IsType<RedirectResult>(await CreateController(withCookie: false)
            .OAuthCallback("code-abc", State, error: null, CancellationToken.None));

        Assert.Contains($"oauthState={State}", redirect.Url);
        _handshakes.Verify(h => h.Attach(State, tokens, "bob@outlook.test"), Times.Once);
    }

    // ---- Complete ----

    [Fact]
    public async Task OAuthComplete_WithAnUnknownOrForeignState_AnswersNotFound()
    {
        _handshakes.Setup(h => h.Consume(State, Uid)).Returns((OAuthHandshake?)null);

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task OAuthComplete_OnAHandshakeTheCallbackNeverFilled_AnswersBadRequest()
    {
        var domain = await CreateOAuthDomainAsync();
        _handshakes.Setup(h => h.Consume(State, Uid)).Returns(Handshake(domain.Id));

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await _accounts.ListAsync(Uid, CancellationToken.None));
    }

    [Fact]
    public async Task OAuthComplete_WithoutACookie_Answers401AndConsumesNothing()
    {
        var result = await CreateController(withCookie: false).OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        _handshakes.Verify(h => h.Consume(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task OAuthComplete_OnAGoodHandshake_CreatesAnOAuthRowTheSessionKeyOpens()
    {
        var domain = await CreateOAuthDomainAsync();
        _handshakes.Setup(h => h.Consume(State, Uid))
            .Returns(Handshake(domain.Id, tokens: Tokens(), email: "alice@outlook.test"));

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ConnectedAccountResponse>(ok.Value);
        Assert.Equal(MailAuthMode.OAuth2, response.AuthMode);
        Assert.Equal("alice@outlook.test", response.Email);
        Assert.Equal("Outlook", response.DomainName);
        Assert.True(response.CredentialsValid);

        var stored = Assert.Single(await _accounts.ListAsync(Uid, CancellationToken.None));
        Assert.Equal(MailAuthMode.OAuth2, stored.AuthMode);
        Assert.Equal("rt", ConnectedAccountCipher.Decrypt(
            _kek, stored.Cipher, ConnectedAccountCipher.Context(stored)).Value);
        // Bound to the OAuth context specifically: the password context must not open it.
        Assert.True(ConnectedAccountCipher.Decrypt(
            _kek, stored.Cipher,
            ConnectedAccountCipher.Context(stored.Id, Uid, domain.Id, stored.Email)).IsFailure);
    }

    [Fact]
    public async Task OAuthComplete_OfAnAlreadyConnectedMailbox_AnswersBadRequest()
    {
        var domain = await CreateOAuthDomainAsync();
        await ConnectedAsync("alice@outlook.test", domain.Id, MailAuthMode.OAuth2);
        _handshakes.Setup(h => h.Consume(State, Uid))
            .Returns(Handshake(domain.Id, tokens: Tokens(), email: "alice@outlook.test"));

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Single(await _accounts.ListAsync(Uid, CancellationToken.None));
    }

    // The caller's own address on an external domain is a different mailbox, exactly as the
    // password flow already rules — the home-server self-connection refusal cannot apply here,
    // since a handshake always names an external domain.
    [Fact]
    public async Task OAuthComplete_WithTheCallersOwnAddressOnAnExternalDomain_CreatesTheRow()
    {
        var domain = await CreateOAuthDomainAsync();
        _handshakes.Setup(h => h.Consume(State, Uid))
            .Returns(Handshake(domain.Id, tokens: Tokens(), email: "alice@weesky.be"));

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task OAuthComplete_WithAnOversizedRefreshToken_AnswersBadRequest()
    {
        var domain = await CreateOAuthDomainAsync();
        var oversized = new string('a', ConnectedAccountCipher.MaxSecretLength + 1);
        _handshakes.Setup(h => h.Consume(State, Uid))
            .Returns(Handshake(
                domain.Id, tokens: Tokens(refreshToken: oversized), email: "alice@outlook.test"));

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await _accounts.ListAsync(Uid, CancellationToken.None));
    }

    [Fact]
    public async Task OAuthComplete_ReconnectingWithADifferentMailbox_AnswersBadRequestAndWritesNothing()
    {
        var domain = await CreateOAuthDomainAsync();
        var row = await ConnectedAsync("alice@outlook.test", domain.Id, MailAuthMode.OAuth2);
        _handshakes.Setup(h => h.Consume(State, Uid))
            .Returns(Handshake(domain.Id, row.Id, Tokens(), "other@outlook.test"));

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        var stored = await _accounts.FindAsync(Uid, row.Id, CancellationToken.None);
        Assert.Equal("rt-old", ConnectedAccountCipher.Decrypt(
            _kek, stored!.Cipher, ConnectedAccountCipher.Context(stored)).Value);
    }

    [Fact]
    public async Task OAuthComplete_ReconnectingTheSameMailbox_ReplacesTheCipher()
    {
        var domain = await CreateOAuthDomainAsync();
        var row = await ConnectedAsync("alice@outlook.test", domain.Id, MailAuthMode.OAuth2);
        _handshakes.Setup(h => h.Consume(State, Uid))
            .Returns(Handshake(domain.Id, row.Id, Tokens(refreshToken: "rt-new"), "alice@outlook.test"));

        var result = await CreateController().OAuthComplete(
            new OAuthCompleteRequest(State), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(row.Id, Assert.IsType<ConnectedAccountResponse>(ok.Value).Id);
        var stored = await _accounts.FindAsync(Uid, row.Id, CancellationToken.None);
        Assert.Equal("rt-new", ConnectedAccountCipher.Decrypt(
            _kek, stored!.Cipher, ConnectedAccountCipher.Context(stored)).Value);
    }

    // ---- The password endpoints against OAuth rows and domains ----

    [Fact]
    public async Task UpdatePassword_OnAnOAuthRow_AnswersBadRequestAndKeepsTheCipher()
    {
        var domain = await CreateOAuthDomainAsync();
        var row = await ConnectedAsync("alice@outlook.test", domain.Id, MailAuthMode.OAuth2);

        var result = await CreateController().UpdatePassword(
            row.Id, new ConnectedAccountPasswordRequest("typed-password"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        AssertNeverOpened();
        var stored = await _accounts.FindAsync(Uid, row.Id, CancellationToken.None);
        Assert.Equal("rt-old", ConnectedAccountCipher.Decrypt(
            _kek, stored!.Cipher, ConnectedAccountCipher.Context(stored)).Value);
    }

    [Fact]
    public async Task Connect_WithAPasswordOnAnOAuthDomain_AnswersBadRequestWithoutProbing()
    {
        var domain = await CreateOAuthDomainAsync();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(domain.Id, "alice@outlook.test", "typed-password"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertNeverOpened();
        Assert.Empty(await _accounts.ListAsync(Uid, CancellationToken.None));
    }

    // ---- The mode on the read surfaces ----

    [Fact]
    public async Task List_AnswersTheAuthModeOnEveryRow()
    {
        var domain = await CreateOAuthDomainAsync();
        var oauthRow = await ConnectedAsync("alice@outlook.test", domain.Id, MailAuthMode.OAuth2);
        var passwordRow = await ConnectedAsync("shared@weesky.be", null, MailAuthMode.Password);

        var result = await CreateController().List(CancellationToken.None);

        var body = Assert.IsAssignableFrom<IReadOnlyList<ConnectedAccountResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MailAuthMode.OAuth2, body.Single(a => a.Id == oauthRow.Id).AuthMode);
        Assert.Equal(MailAuthMode.Password, body.Single(a => a.Id == passwordRow.Id).AuthMode);
    }

    [Fact]
    public async Task Domains_AnswerTheAuthModeOnEveryChoice()
    {
        var oauthDomain = await CreateOAuthDomainAsync();
        var passwordDomain = await CreatePasswordDomainAsync();

        var result = await CreateController(withCookie: false).Domains(CancellationToken.None);

        var choices = Assert.IsAssignableFrom<IReadOnlyList<ExternalDomainChoice>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MailAuthMode.OAuth2, choices.Single(c => c.Id == oauthDomain.Id).AuthMode);
        Assert.Equal(MailAuthMode.Password, choices.Single(c => c.Id == passwordDomain.Id).AuthMode);
    }
}
