using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class AccountControllerTests
{
    private const string OldPassword = "OldPass";
    private const string NewPassword = "NewPass123!";

    // Derived once for the whole class: 600k PBKDF2 iterations are not free.
    private static readonly byte[] TestSalt = ConnectedAccountCipher.NewSalt();
    private static readonly byte[] OldKek = ConnectedAccountCipher.DeriveKek(OldPassword, TestSalt);
    private static readonly byte[] NewKek = ConnectedAccountCipher.DeriveKek(NewPassword, TestSalt);

    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IUsersRepository> _usersRepo = new();
    private readonly Mock<IDovecotQuotaClient> _dovecotClient = new();
    private readonly Mock<IMailCredentialStore> _credentials = new();
    private readonly Mock<IWebmailUserStore> _webmailUsers = new();
    private readonly Mock<IConnectedAccountStore> _connectedAccounts = new();
    private readonly Mock<ISessionGuard> _sessions = new();
    private readonly Mock<ITokenManager> _tokens = new();

    public AccountControllerTests()
    {
        _webmailUsers.Setup(s => s.GetOrCreateKdfSaltAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(TestSalt);
        _connectedAccounts.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync([]);
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Success(new MailCredentialPayload(OldPassword, OldKek)));
    }

    private AccountController CreateController()
    {
        _tokens.Setup(t => t.Generate(It.IsAny<User>()))
               .Returns(new AuthToken { ExpiresIn = 2880, Token = "renewed.jwt" });

        var controller = new AccountController(
            _usersRepo.Object, _dovecotClient.Object, _credentials.Object,
            _webmailUsers.Object, _connectedAccounts.Object, _sessions.Object, _tokens.Object,
            Options.Create(new TokenConstants { ExpiryInMinutes = 2880, AuthCookieName = "BearerAuth" }),
            NullLogger<AccountController>.Instance);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", UserId);
        return controller;
    }

    private static ConnectedAccount Connected(string email, byte[] cipher) =>
        new() { Id = Guid.NewGuid(), UserId = UserId, Email = email, Cipher = cipher };

    [Fact]
    public async Task GetAccountInfo_WhenUserFound_Returns200WithAccountInfo()
    {
        var accountInfo = new AccountInfo { UserId = 1, UserName = "john" };
        _usersRepo.Setup(r => r.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(accountInfo));

        var result = await CreateController().GetAccountInfo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accountInfo, ok.Value);
    }

    [Fact]
    public async Task GetAccountInfo_WhenUserNotFound_Returns404WithEnvelope()
    {
        _usersRepo.Setup(r => r.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AccountInfo>("Account not found"));

        var result = await CreateController().GetAccountInfo(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Account not found", envelope.Message);
    }

    [Fact]
    public async Task GetQuota_WhenSuccess_Returns200WithQuota()
    {
        var quota = new Quota { StorageBytesUsed = 1024, MessageCount = 5 };
        _dovecotClient.Setup(c => c.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(quota));

        var result = await CreateController().GetQuota(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(quota, ok.Value);
    }

    [Fact]
    public async Task GetQuota_WhenFailed_Returns502WithEnvelope()
    {
        _dovecotClient.Setup(c => c.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Quota>("Unreachable"));

        var result = await CreateController().GetQuota(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(502, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Unreachable", envelope.Message);
    }

    [Fact]
    public async Task GetFolders_WhenSuccess_Returns200WithFolderList()
    {
        IReadOnlyList<string> folders = ["INBOX", "Sent", "Trash"];
        _dovecotClient.Setup(c => c.GetMailboxesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(folders));

        var result = await CreateController().GetFolders(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(folders, ok.Value);
    }

    [Fact]
    public async Task GetFolders_WhenFailed_Returns502WithEnvelope()
    {
        _dovecotClient.Setup(c => c.GetMailboxesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<string>>("Unreachable"));

        var result = await CreateController().GetFolders(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(502, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Unreachable", envelope.Message);
    }

    [Fact]
    public async Task ChangePassword_WhenSuccess_Returns204()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "OldPass" }, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(204, status.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WhenFailed_Returns400WithEnvelope()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Invalid password"));

        var result = await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WhenFailed_EnvelopeContainsErrorMessage()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Invalid password"));

        var result = await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Invalid password", envelope.Message);
        Assert.Equal(ResultState.Error, envelope.State);
    }

    // The credentials cookie holds the password every mail endpoint opens IMAP with. Left on the
    // superseded one, the session stays authenticated but every mail action fails for up to the
    // token's whole lifetime — the sliding renewal keeps re-storing the stale value.
    [Fact]
    public async Task ChangePassword_WhenSuccess_ReissuesTheCredentialsCookieWithTheNewPassword()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await CreateController().ChangePassword(new SecretChange { NewPassword = NewPassword, OldPassword = OldPassword }, CancellationToken.None);

        _credentials.Verify(
            c => c.Store(It.IsAny<HttpResponse>(), It.Is<MailCredentialPayload>(p => p.Password == NewPassword),
                TimeSpan.FromMinutes(2880)),
            Times.Once);
    }

    // The cookie must come back carrying the key the new password derives, not the superseded one:
    // every later request reads the KEK from it rather than paying 600k iterations again.
    [Fact]
    public async Task ChangePassword_StoresTheNewPayload()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        MailCredentialPayload? stored = null;
        _credentials.Setup(c => c.Store(It.IsAny<HttpResponse>(), It.IsAny<MailCredentialPayload>(), It.IsAny<TimeSpan>()))
                    .Callback<HttpResponse, MailCredentialPayload, TimeSpan>((_, p, _) => stored = p);

        await CreateController().ChangePassword(new SecretChange { NewPassword = NewPassword, OldPassword = OldPassword }, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(NewPassword, stored.Password);
        Assert.Equal<byte[]>(NewKek, stored.Kek!);
    }

    // The connected-account ciphers hang off the old main password. Left alone they would all be
    // undecryptable the moment it changes — every attached mailbox silently dead.
    [Fact]
    public async Task ChangePassword_ReEncryptsEveryConnectedAccountCipher()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var first = Connected("a@external.com", ConnectedAccountCipher.Encrypt(OldKek, "secret-a"));
        var second = Connected("b@external.com", ConnectedAccountCipher.Encrypt(OldKek, "secret-b"));
        _connectedAccounts.Setup(s => s.ListAsync(UserId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync([first, second]);
        IReadOnlyDictionary<Guid, byte[]>? replaced = null;
        _connectedAccounts.Setup(s => s.ReplaceCiphersAsync(UserId, It.IsAny<IReadOnlyDictionary<Guid, byte[]>>(), It.IsAny<CancellationToken>()))
                          .Callback<Guid, IReadOnlyDictionary<Guid, byte[]>, CancellationToken>((_, c, _) => replaced = c)
                          .Returns(Task.CompletedTask);

        await CreateController().ChangePassword(new SecretChange { NewPassword = NewPassword, OldPassword = OldPassword }, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.Equal(2, replaced.Count);
        Assert.Equal("secret-a", ConnectedAccountCipher.Decrypt(NewKek, replaced[first.Id]).Value);
        Assert.Equal("secret-b", ConnectedAccountCipher.Decrypt(NewKek, replaced[second.Id]).Value);
    }

    // A row already orphaned by an out-of-band password change stays as it is: re-encrypting
    // garbage would make it permanently unreadable, deleting it would lose the address.
    [Fact]
    public async Task ChangePassword_LeavesAnUndecryptableCipherUntouched()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var live = Connected("a@external.com", ConnectedAccountCipher.Encrypt(OldKek, "secret-a"));
        var orphan = Connected("b@external.com", ConnectedAccountCipher.Encrypt(NewKek, "unreachable"));
        _connectedAccounts.Setup(s => s.ListAsync(UserId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync([live, orphan]);
        IReadOnlyDictionary<Guid, byte[]>? replaced = null;
        _connectedAccounts.Setup(s => s.ReplaceCiphersAsync(UserId, It.IsAny<IReadOnlyDictionary<Guid, byte[]>>(), It.IsAny<CancellationToken>()))
                          .Callback<Guid, IReadOnlyDictionary<Guid, byte[]>, CancellationToken>((_, c, _) => replaced = c)
                          .Returns(Task.CompletedTask);

        await CreateController().ChangePassword(new SecretChange { NewPassword = NewPassword, OldPassword = OldPassword }, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.Equal([live.Id], replaced.Keys);
        _connectedAccounts.Verify(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // A cookie issued before the KEK existed carries none; the old key is then derived from the
    // old password the request already supplies, so a session open across the deploy still re-keys.
    [Fact]
    public async Task ChangePassword_DerivesTheOldKekWhenTheCookieIsStillV1()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Success(new MailCredentialPayload(OldPassword, null)));
        var account = Connected("a@external.com", ConnectedAccountCipher.Encrypt(OldKek, "secret-a"));
        _connectedAccounts.Setup(s => s.ListAsync(UserId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync([account]);
        IReadOnlyDictionary<Guid, byte[]>? replaced = null;
        _connectedAccounts.Setup(s => s.ReplaceCiphersAsync(UserId, It.IsAny<IReadOnlyDictionary<Guid, byte[]>>(), It.IsAny<CancellationToken>()))
                          .Callback<Guid, IReadOnlyDictionary<Guid, byte[]>, CancellationToken>((_, c, _) => replaced = c)
                          .Returns(Task.CompletedTask);

        await CreateController().ChangePassword(new SecretChange { NewPassword = NewPassword, OldPassword = OldPassword }, CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.Equal("secret-a", ConnectedAccountCipher.Decrypt(NewKek, replaced[account.Id]).Value);
    }

    [Fact]
    public async Task ChangePassword_WhenFailed_LeavesTheCredentialsCookieAlone()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Invalid password"));

        await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" }, CancellationToken.None);

        _credentials.Verify(c => c.Store(It.IsAny<HttpResponse>(), It.IsAny<MailCredentialPayload>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    // Trap 1. Rotating cuts every session of the account — including the one making the request.
    // Without a fresh JWT in the same response, changing your password would sign you out, which
    // is the bug 1.3 was about, in a new costume.
    [Fact]
    public async Task ChangePassword_WhenSuccess_RevokesEverySessionButReissuesThisOne()
    {
        var rotated = Guid.NewGuid();
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _webmailUsers.Setup(s => s.RotateSecurityStampAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rotated);

        var controller = CreateController();
        await controller.ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "OldPass" }, CancellationToken.None);

        _webmailUsers.Verify(s => s.RotateSecurityStampAsync("john@example.com", It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(s => s.Forget("john@example.com"), Times.Once);
        _tokens.Verify(t => t.Generate(It.Is<User>(u => u.SecurityStamp == rotated)), Times.Once);
        Assert.Contains("BearerAuth=renewed.jwt",
            string.Join(";", controller.Response.Headers["Set-Cookie"].ToArray()));
    }

    [Fact]
    public async Task ChangePassword_WhenFailed_RevokesNothing()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Invalid password"));

        await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" }, CancellationToken.None);

        _webmailUsers.Verify(s => s.RotateSecurityStampAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessions.Verify(s => s.Forget(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ChangeFullName_WhenSuccess_Returns204()
    {
        _usersRepo.Setup(r => r.ChangeFullNameAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().ChangeFullName(new FullNameChange { FullName = "John Doe" }, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(204, status.StatusCode);
    }

    [Fact]
    public async Task ChangeFullName_WhenFailed_Returns400WithEnvelope()
    {
        _usersRepo.Setup(r => r.ChangeFullNameAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("User not found"));

        var result = await CreateController().ChangeFullName(new FullNameChange { FullName = "John Doe" }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("User not found", envelope.Message);
    }

    // A password that is already committed cannot be left with cookies holding the old one: the
    // caller's token governs the write, and nothing after it.
    [Fact]
    public async Task ChangePassword_WhenTheCallerDisconnects_StillRunsTheCompensatingWork()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await CreateController().ChangePassword(
            new SecretChange { NewPassword = NewPassword, OldPassword = OldPassword }, cts.Token);

        _usersRepo.Verify(r => r.ChangePasswordAsync(It.IsAny<User>(), NewPassword, OldPassword, cts.Token), Times.Once);
        _webmailUsers.Verify(s => s.RotateSecurityStampAsync(
            It.IsAny<string>(), It.Is<CancellationToken>(t => !t.IsCancellationRequested)), Times.Once);
        _connectedAccounts.Verify(s => s.ListAsync(
            UserId, It.Is<CancellationToken>(t => !t.IsCancellationRequested)), Times.Once);
        _credentials.Verify(c => c.Store(
            It.IsAny<HttpResponse>(), It.IsAny<MailCredentialPayload>(), It.IsAny<TimeSpan>()), Times.Once);
    }
}
