using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// Real stores over an in-memory database and a real credential store: the mocked boundary is the
/// IMAP factory alone, because whether a connection is opened — and with what — is the claim this
/// controller has to make. Nothing here may put a secret on the wire or in a log.
/// </summary>
public sealed class ConnectedAccountsControllerTests
{
    private const string MainPassword = "hunter2";
    private const string OwnEmail = "alice@weesky.be";

    private static readonly Guid Uid = Guid.NewGuid();

    private readonly MailCredentialStore _credentials = new(new EphemeralDataProtectionProvider());
    private readonly Mock<IImapConnectionFactory> _imap = new();
    private readonly ConnectedAccountStore _accounts;
    private readonly ExternalDomainStore _domains;
    private readonly SendingIdentityStore _identities;
    private readonly WebmailUserStore _users;

    // Arranging through a second context over the same store is what production does — one context
    // per request — and without it the controller's update attaches over an already tracked row.
    private readonly ConnectedAccountStore _arrangedAccounts;
    private readonly SendingIdentityStore _arrangedIdentities;

    private readonly byte[] _kek =
        ConnectedAccountCipher.DeriveKek(MainPassword, ConnectedAccountCipher.NewSalt());

    public ConnectedAccountsControllerTests()
    {
        var databaseName = Guid.NewGuid().ToString();
        var db = new PreferencesTestDbContext(databaseName);
        var arranged = new PreferencesTestDbContext(databaseName);

        _accounts = new ConnectedAccountStore(db);
        _domains = new ExternalDomainStore(db);
        _identities = new SendingIdentityStore(db);
        _users = new WebmailUserStore(db);

        _arrangedAccounts = new ConnectedAccountStore(arranged);
        _arrangedIdentities = new SendingIdentityStore(arranged);
    }

    private ConnectedAccountsController CreateController(bool withCookie = true)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(TestConnections.HomeOptions());

        var controller = new ConnectedAccountsController(
            _accounts, _domains, _identities, _credentials, _users, _imap.Object, monitor.Object,
            NullLogger<ConnectedAccountsController>.Instance)
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

    private Mock<IImapSession> ProbeSucceeds()
    {
        var session = new Mock<IImapSession>();
        _imap.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success(session.Object));
        return session;
    }

    private void ProbeFails() =>
        _imap.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Failure<IImapSession>(
                 "AUTHENTICATIONFAILED: bad password for alice@gmail.test"));

    private async Task<ConnectedAccount> ConnectedAsync(
        string email, string secret = "secret", Guid? domainId = null, Guid? ownerId = null, byte[]? kek = null)
    {
        var created = await _arrangedAccounts.CreateAsync(new ConnectedAccount
        {
            UserId = ownerId ?? Uid,
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

    private void AssertNeverOpened() => _imap.Verify(
        f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);

    // ---- Domains choice list ----

    [Fact]
    public async Task Domains_CarriesNamesAndIdsOnly()
    {
        var domain = await CreateDomainAsync();

        var result = await CreateController(withCookie: false).Domains(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var choice = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ExternalDomainChoice>>(ok.Value));
        Assert.Equal(domain.Id, choice.Id);
        Assert.Equal("Gmail", choice.Name);

        // The claim is that no configuration field is serialised at all — asserted on the property
        // names, since a port number can turn up by chance inside the id's hex digits.
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("gmail.test", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("security", json, StringComparison.OrdinalIgnoreCase);
        foreach (var field in new[]
                 {
                     "imapHost", "imapPort", "smtpHost", "smtpPort",
                     "imapSecurity", "smtpSecurity", "sieveHost", "sievePort"
                 })
            Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- List ----

    [Fact]
    public async Task List_WithoutACookie_Returns401()
    {
        var result = await CreateController(withCookie: false).List(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal("credentials_unavailable",
            Assert.IsType<ResultEnveloppe>(unauthorized.Value).Message);
    }

    [Fact]
    public async Task List_DescribesEachAccount()
    {
        var domain = await CreateDomainAsync(d => d.SieveHost = null);
        var external = await ConnectedAsync("alice@gmail.test", domainId: domain.Id);
        var local = await ConnectedAsync("shared@weesky.be");

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsAssignableFrom<IReadOnlyList<ConnectedAccountResponse>>(ok.Value);

        var gmail = body.Single(a => a.Id == external.Id);
        Assert.Equal("alice@gmail.test", gmail.Email);
        Assert.Equal(domain.Id, gmail.DomainId);
        Assert.Equal("Gmail", gmail.DomainName);
        Assert.False(gmail.SieveSupported);
        Assert.True(gmail.CredentialsValid);

        var shared = body.Single(a => a.Id == local.Id);
        Assert.Null(shared.DomainId);
        Assert.Null(shared.DomainName);
        Assert.True(shared.SieveSupported);
    }

    [Fact]
    public async Task List_TakesTheDisplayNameFromTheDefaultIdentity()
    {
        var account = await ConnectedAsync("shared@weesky.be");
        await _arrangedIdentities.ReplaceAsync(Uid, account.Id.ToString(),
            [new SendingIdentity { Address = "shared@weesky.be", DisplayName = "Support", IsDefault = true }],
            CancellationToken.None);

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsAssignableFrom<IReadOnlyList<ConnectedAccountResponse>>(ok.Value);
        Assert.Equal("Support", Assert.Single(body).DisplayName);
    }

    // The settings page lists every attached mailbox at once: one IMAP dialogue per row would make
    // it take seconds and hammer the providers, so validity is a local decrypt and nothing more.
    [Fact]
    public async Task List_ReportsInvalidCredentialsWithoutOpeningAnyConnection()
    {
        var stale = ConnectedAccountCipher.DeriveKek("old", ConnectedAccountCipher.NewSalt());
        await ConnectedAsync("shared@weesky.be", kek: stale);

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsAssignableFrom<IReadOnlyList<ConnectedAccountResponse>>(ok.Value);
        Assert.False(Assert.Single(body).CredentialsValid);
        AssertNeverOpened();
    }

    [Fact]
    public async Task List_IgnoresAnotherUsersAccounts()
    {
        await ConnectedAsync("other@weesky.be", ownerId: Guid.NewGuid());

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ConnectedAccountResponse>>(ok.Value));
    }

    // ---- Connect ----

    [Fact]
    public async Task Connect_VerifiesImapBeforeStoring()
    {
        var domain = await CreateDomainAsync();
        ProbeFails();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(domain.Id, "alice@gmail.test", "gmailpw"), CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, refused.StatusCode);

        // The server's own text never reaches the caller.
        var message = Assert.IsType<ResultEnveloppe>(refused.Value).Message;
        Assert.DoesNotContain("AUTHENTICATIONFAILED", message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(await _accounts.ListAsync(Uid, CancellationToken.None));
    }

    [Fact]
    public async Task Connect_ProbesTheDomainEndpointsAndClosesTheSession()
    {
        var domain = await CreateDomainAsync();
        var session = ProbeSucceeds();

        await CreateController().Connect(
            new ConnectAccountRequest(domain.Id, "alice@gmail.test", "gmailpw"), CancellationToken.None);

        _imap.Verify(f => f.OpenAsync(
            It.Is<MailAccountConnection>(c =>
                c.ImapHost == "imap.gmail.test" && c.ImapPort == 993 &&
                c.Username == "alice@gmail.test" && c.Password == "gmailpw" && !c.IsHomeServer),
            It.IsAny<CancellationToken>()), Times.Once);
        session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Connect_ALocalMailbox_ProbesTheHomeEndpoints()
    {
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, "Shared@Weesky.be", "sharedpw"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        _imap.Verify(f => f.OpenAsync(
            It.Is<MailAccountConnection>(c => c.ImapHost == "imap.home.test" && c.IsHomeServer),
            It.IsAny<CancellationToken>()), Times.Once);

        // Canonicalised on the way in, like every address in this database.
        var stored = Assert.Single(await _accounts.ListAsync(Uid, CancellationToken.None));
        Assert.Equal("shared@weesky.be", stored.Email);
    }

    [Fact]
    public async Task Connect_StoresACipherTheSessionKeyOpens()
    {
        ProbeSucceeds();

        await CreateController().Connect(
            new ConnectAccountRequest(null, "shared@weesky.be", "sharedpw"), CancellationToken.None);

        var stored = Assert.Single(await _accounts.ListAsync(Uid, CancellationToken.None));
        Assert.Equal("sharedpw", ConnectedAccountCipher.Decrypt(_kek, stored.Cipher).Value);
    }

    [Fact]
    public async Task Connect_RefusesTheCallersOwnMailbox()
    {
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, "Alice@Weesky.be", "hunter2"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertNeverOpened();
        Assert.Empty(await _accounts.ListAsync(Uid, CancellationToken.None));
    }

    // The same address on an external domain is a different mailbox, so only the local case is refused.
    [Fact]
    public async Task Connect_AcceptsTheSameAddressOnAnExternalDomain()
    {
        var domain = await CreateDomainAsync();
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(domain.Id, OwnEmail, "pw"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Connect_RefusesADuplicate()
    {
        await ConnectedAsync("shared@weesky.be");
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, "shared@weesky.be", "sharedpw"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Single(await _accounts.ListAsync(Uid, CancellationToken.None));
    }

    [Fact]
    public async Task Connect_CreatesTheDefaultIdentity()
    {
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, "shared@weesky.be", "sharedpw"), CancellationToken.None);

        var created = Assert.IsType<ConnectedAccountResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        var identity = Assert.Single(
            await _identities.GetAsync(Uid, created.Id.ToString(), CancellationToken.None));
        Assert.Equal("shared@weesky.be", identity.Address);
        Assert.True(identity.IsDefault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public async Task Connect_RefusesAnUnusableAddress(string email)
    {
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, email, "pw"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertNeverOpened();
    }

    [Fact]
    public async Task Connect_RefusesAnEmptyPassword()
    {
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, "shared@weesky.be", ""), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertNeverOpened();
    }

    // The cipher column is finite and Encrypt throws past it: the bound is answered, never thrown.
    [Fact]
    public async Task Connect_RefusesAPasswordPastTheCipherBound()
    {
        ProbeSucceeds();
        var tooLong = new string('a', ConnectedAccountCipher.MaxSecretLength + 1);

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, "shared@weesky.be", tooLong), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertNeverOpened();
    }

    // The address column is finite the same way the cipher column is; reaching this needs a server
    // that authenticates a 250-character login, so it is depth rather than a live path.
    [Fact]
    public async Task Connect_RefusesAnAddressPastTheColumnWidth()
    {
        ProbeSucceeds();
        var local = new string('a', ConnectedAccountsController.MaxEmailLength);

        var result = await CreateController().Connect(
            new ConnectAccountRequest(null, $"{local}@weesky.be", "pw"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertNeverOpened();
    }

    [Fact]
    public async Task Connect_RefusesAnUnknownDomain()
    {
        ProbeSucceeds();

        var result = await CreateController().Connect(
            new ConnectAccountRequest(Guid.NewGuid(), "alice@gmail.test", "pw"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertNeverOpened();
    }

    [Fact]
    public async Task Connect_WithoutACookie_Returns401()
    {
        ProbeSucceeds();

        var result = await CreateController(withCookie: false).Connect(
            new ConnectAccountRequest(null, "shared@weesky.be", "pw"), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        AssertNeverOpened();
    }

    [Fact]
    public async Task Responses_CarryNoSecret()
    {
        ProbeSucceeds();

        var connect = await CreateController().Connect(
            new ConnectAccountRequest(null, "shared@weesky.be", "sharedpw"), CancellationToken.None);
        var created = Assert.IsType<OkObjectResult>(connect.Result).Value;

        var list = await CreateController().List(CancellationToken.None);
        var listed = Assert.IsType<OkObjectResult>(list.Result).Value;

        foreach (var payload in new[] { created, listed })
        {
            var json = JsonSerializer.Serialize(payload);
            Assert.DoesNotContain("cipher", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sharedpw", json, StringComparison.Ordinal);
            Assert.DoesNotContain("kek", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("salt", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Password ----

    [Fact]
    public async Task UpdatePassword_ReEncrypts()
    {
        var account = await ConnectedAsync("shared@weesky.be", "oldpw");
        ProbeSucceeds();

        var result = await CreateController().UpdatePassword(
            account.Id, new ConnectedAccountPasswordRequest("newpw"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var stored = await _accounts.FindAsync(Uid, account.Id, CancellationToken.None);
        Assert.Equal("newpw", ConnectedAccountCipher.Decrypt(_kek, stored!.Cipher).Value);
    }

    [Fact]
    public async Task UpdatePassword_ProbesWithTheNewPassword()
    {
        var account = await ConnectedAsync("shared@weesky.be", "oldpw");
        ProbeSucceeds();

        await CreateController().UpdatePassword(
            account.Id, new ConnectedAccountPasswordRequest("newpw"), CancellationToken.None);

        _imap.Verify(f => f.OpenAsync(
            It.Is<MailAccountConnection>(c => c.Password == "newpw" && c.Username == "shared@weesky.be"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePassword_WhenTheServerRefuses_KeepsThePreviousCipher()
    {
        var account = await ConnectedAsync("shared@weesky.be", "oldpw");
        ProbeFails();

        var result = await CreateController().UpdatePassword(
            account.Id, new ConnectedAccountPasswordRequest("newpw"), CancellationToken.None);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, refused.StatusCode);

        var stored = await _accounts.FindAsync(Uid, account.Id, CancellationToken.None);
        Assert.Equal("oldpw", ConnectedAccountCipher.Decrypt(_kek, stored!.Cipher).Value);
    }

    [Fact]
    public async Task UpdatePassword_ForAForeignAccount_Returns404()
    {
        var foreign = await ConnectedAsync("other@weesky.be", ownerId: Guid.NewGuid());
        ProbeSucceeds();

        var result = await CreateController().UpdatePassword(
            foreign.Id, new ConnectedAccountPasswordRequest("newpw"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        AssertNeverOpened();
    }

    [Fact]
    public async Task UpdatePassword_RefusesAPasswordPastTheCipherBound()
    {
        var account = await ConnectedAsync("shared@weesky.be", "oldpw");
        ProbeSucceeds();

        var result = await CreateController().UpdatePassword(
            account.Id, new ConnectedAccountPasswordRequest(new string('a', ConnectedAccountCipher.MaxSecretLength + 1)),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        AssertNeverOpened();
    }

    [Fact]
    public async Task UpdatePassword_WithoutACookie_Returns401()
    {
        var account = await ConnectedAsync("shared@weesky.be", "oldpw");

        var result = await CreateController(withCookie: false).UpdatePassword(
            account.Id, new ConnectedAccountPasswordRequest("newpw"), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        AssertNeverOpened();
    }

    // ---- Disconnect ----

    [Fact]
    public async Task Delete_AnswersNotFoundForAForeignAccount()
    {
        var foreign = await ConnectedAsync("other@weesky.be", ownerId: Guid.NewGuid());

        var result = await CreateController().Disconnect(foreign.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(await _accounts.FindAsync(foreign.UserId, foreign.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheAccountAndItsIdentities()
    {
        var account = await ConnectedAsync("shared@weesky.be");

        var result = await CreateController().Disconnect(account.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _accounts.ListAsync(Uid, CancellationToken.None));
        Assert.Empty(await _identities.GetAsync(Uid, account.Id.ToString(), CancellationToken.None));
    }
}
