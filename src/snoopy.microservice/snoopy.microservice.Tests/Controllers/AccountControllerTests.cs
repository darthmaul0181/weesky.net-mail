using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class AccountControllerTests
{
    private readonly Mock<IUsersRepository> _usersRepo = new();
    private readonly Mock<IDovecotQuotaClient> _dovecotClient = new();
    private readonly Mock<IMailCredentialStore> _credentials = new();

    private AccountController CreateController()
    {
        var controller = new AccountController(
            _usersRepo.Object, _dovecotClient.Object, _credentials.Object,
            Options.Create(new TokenConstants { ExpiryInMinutes = 2880, AuthCookieName = "BearerAuth" }));
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
        return controller;
    }

    [Fact]
    public async Task GetAccountInfo_WhenUserFound_Returns200WithAccountInfo()
    {
        var accountInfo = new AccountInfo { UserId = 1, UserName = "john" };
        _usersRepo.Setup(r => r.GetAccountInfoAsync(It.IsAny<User>()))
            .ReturnsAsync(Result.Success(accountInfo));

        var result = await CreateController().GetAccountInfo();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accountInfo, ok.Value);
    }

    [Fact]
    public async Task GetAccountInfo_WhenUserNotFound_Returns404WithEnvelope()
    {
        _usersRepo.Setup(r => r.GetAccountInfoAsync(It.IsAny<User>()))
            .ReturnsAsync(Result.Failure<AccountInfo>("Account not found"));

        var result = await CreateController().GetAccountInfo();

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
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "OldPass" });

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(204, status.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WhenFailed_Returns400WithEnvelope()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure("Invalid password"));

        var result = await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WhenFailed_EnvelopeContainsErrorMessage()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure("Invalid password"));

        var result = await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" });

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
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success());

        await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "OldPass" });

        _credentials.Verify(c => c.Store(It.IsAny<HttpResponse>(), "NewPass123!", TimeSpan.FromMinutes(2880)), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_WhenFailed_LeavesTheCredentialsCookieAlone()
    {
        _usersRepo.Setup(r => r.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure("Invalid password"));

        await CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" });

        _credentials.Verify(c => c.Store(It.IsAny<HttpResponse>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task ChangeFullName_WhenSuccess_Returns204()
    {
        _usersRepo.Setup(r => r.ChangeFullNameAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().ChangeFullName(new FullNameChange { FullName = "John Doe" });

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(204, status.StatusCode);
    }

    [Fact]
    public async Task ChangeFullName_WhenFailed_Returns400WithEnvelope()
    {
        _usersRepo.Setup(r => r.ChangeFullNameAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure("User not found"));

        var result = await CreateController().ChangeFullName(new FullNameChange { FullName = "John Doe" });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("User not found", envelope.Message);
    }
}
