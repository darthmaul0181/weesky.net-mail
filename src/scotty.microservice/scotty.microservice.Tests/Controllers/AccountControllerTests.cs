using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Platform;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class AccountControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("john@example.com", "hunter2");

    private readonly Mock<IAccountInfoProvider> _accountInfo = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();
    private readonly Mock<IImapSessionProvider> _imapSessions = new();
    private readonly Mock<IImapSession> _imapSession = new();

    public AccountControllerTests()
    {
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(Conn));
        _imapSessions.Setup(s => s.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Success<IImapSession>(_imapSession.Object));
        _imapSession.SetupGet(s => s.SupportsQuota).Returns(true);
    }

    private AccountController CreateController(string platform = PlatformOptions.Weesky)
    {
        var controller = new AccountController(
            Options.Create(new PlatformOptions { Platform = platform }), _accountInfo.Object, _connections.Object, _imapSessions.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", UserId);
        return controller;
    }

    [Fact]
    public async Task GetAccountInfo_WhenUserFound_Returns200WithAccountInfo()
    {
        var accountInfo = new AccountInfo { UserId = 1, UserName = "john" };
        _accountInfo.Setup(r => r.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(accountInfo));

        var result = await CreateController().GetAccountInfo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accountInfo, ok.Value);
    }

    /// <summary>The admin page picks its tabs from this: it must not wait on, nor fail with, the IMAP
    /// session that capabilities open.</summary>
    [Theory]
    [InlineData(PlatformOptions.Weesky)]
    [InlineData(PlatformOptions.Generic)]
    public async Task GetAccountInfo_CarriesThePlatform(string platform)
    {
        _accountInfo.Setup(r => r.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AccountInfo { UserId = 1, UserName = "john" }));

        var result = await CreateController(platform).GetAccountInfo(CancellationToken.None);

        Assert.Equal(platform, Assert.IsType<AccountInfo>(Assert.IsType<OkObjectResult>(result.Result).Value).Platform);
    }

    [Fact]
    public async Task GetAccountInfo_WhenUserNotFound_Returns404WithEnvelope()
    {
        _accountInfo.Setup(r => r.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AccountInfo>("Account not found"));

        var result = await CreateController().GetAccountInfo(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Account not found", envelope.Message);
    }

    [Fact]
    public async Task GetQuota_WhenSupported_Returns200WithQuota()
    {
        var quota = new Quota { StorageBytesUsed = 1024, MessageCount = 5 };
        _imapSession.Setup(s => s.GetQuotaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(quota));

        var result = await CreateController().GetQuota(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(quota, ok.Value);
    }

    // A server that never advertised QUOTA (rule 1: nothing about the mail server is
    // configured here — this is read live) has nothing to answer with; 204 rather than an
    // error, since not every IMAP server implements RFC 2087.
    [Fact]
    public async Task GetQuota_WhenNotSupported_Returns204AndNeverAsksTheServer()
    {
        _imapSession.SetupGet(s => s.SupportsQuota).Returns(false);

        var result = await CreateController().GetQuota(CancellationToken.None);

        Assert.IsType<NoContentResult>(result.Result);
        _imapSession.Verify(s => s.GetQuotaAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetQuota_WhenCredentialsUnavailable_Returns401WithEnvelope()
    {
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailAccountConnection>("credentials_unavailable"));

        var result = await CreateController().GetQuota(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ResultEnveloppe>(unauthorized.Value);
        Assert.Equal("credentials_unavailable", envelope.Message);
    }

    [Fact]
    public async Task GetQuota_WhenSessionFails_Returns502WithEnvelope()
    {
        _imapSessions.Setup(s => s.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

        var result = await CreateController().GetQuota(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(502, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Unable to connect to the mail service", envelope.Message);
    }
}
