using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CapabilitiesControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("john@example.com", "hunter2");

    private readonly Mock<IAliasDirectory> _aliasDirectory = new();
    private readonly Mock<IAccountInfoProvider> _accountInfo = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();
    private readonly Mock<IImapSessionProvider> _imapSessions = new();
    private readonly Mock<IImapSession> _imapSession = new();
    private readonly Mock<ISieveAvailabilityProbe> _sieveProbe = new();
    private PlatformOptions _platform = new() { Platform = PlatformOptions.Weesky };
    private readonly SieveOptions _sieve = new() { Host = "sieve.weesky.be", Port = 4190 };

    public CapabilitiesControllerTests()
    {
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(Conn));
        _imapSessions.Setup(s => s.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Success<IImapSession>(_imapSession.Object));
        _imapSession.SetupGet(s => s.SupportsQuota).Returns(true);
        _sieveProbe.Setup(p => p.IsAvailableAsync(_sieve.Host, _sieve.Port, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        _aliasDirectory.SetupGet(d => d.EnforcesOwnership).Returns(true);
        _accountInfo.Setup(a => a.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new AccountInfo { UserId = 1, UserName = "john", IsAdmin = true }));
    }

    private CapabilitiesController CreateController(string? davPublicUrl = null)
    {
        var controller = new CapabilitiesController(
            Options.Create(_platform),
            Options.Create(_sieve),
            Options.Create(new DavOptions { PublicUrl = davPublicUrl }),
            _aliasDirectory.Object,
            _accountInfo.Object,
            _connections.Object,
            _imapSessions.Object,
            _sieveProbe.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", UserId);
        return controller;
    }

    private async Task<CapabilitiesResponse> GetCapabilitiesAsync(string? davPublicUrl = null)
    {
        var result = await CreateController(davPublicUrl).GetCapabilities(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<CapabilitiesResponse>(ok.Value);
    }

    [Fact]
    public async Task GetCapabilities_OnWeeskyAsAdmin_ReturnsEveryFlagTrue()
    {
        var capabilities = await GetCapabilitiesAsync(davPublicUrl: "https://api.mail.weesky.net");

        Assert.Equal(new CapabilitiesResponse(
            Platform: "weesky", Admin: true, Aliases: true, PasswordChange: true, ProfileEditing: true,
            StrictIdentities: true, Quota: true, Rules: true, Dav: true), capabilities);
    }

    [Fact]
    public async Task GetCapabilities_OnWeeskyAsNonAdmin_ReturnsAdminFalse()
    {
        _accountInfo.Setup(a => a.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new AccountInfo { UserId = 1, UserName = "john", IsAdmin = false }));

        var result = await CreateController().GetCapabilities(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var capabilities = Assert.IsType<CapabilitiesResponse>(ok.Value);
        Assert.False(capabilities.Admin);
        Assert.True(capabilities.Aliases);
    }

    // A Result.Failure from the account lookup means "not admin", not an error for the whole
    // response — the caller already learned everything wrong about their session from the
    // connection resolution, which this scenario leaves untouched.
    [Fact]
    public async Task GetCapabilities_WhenAccountLookupFails_ReturnsAdminFalseNotAnError()
    {
        _accountInfo.Setup(a => a.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure<AccountInfo>("Account not found"));

        var result = await CreateController().GetCapabilities(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var capabilities = Assert.IsType<CapabilitiesResponse>(ok.Value);
        Assert.False(capabilities.Admin);
    }

    [Fact]
    public async Task GetCapabilities_OnGeneric_ReturnsThePlatformWiredFlagsFalse()
    {
        _platform = new PlatformOptions { Platform = PlatformOptions.Generic };
        _aliasDirectory.SetupGet(d => d.EnforcesOwnership).Returns(false);

        var result = await CreateController().GetCapabilities(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var capabilities = Assert.IsType<CapabilitiesResponse>(ok.Value);
        Assert.Equal("generic", capabilities.Platform);
        Assert.False(capabilities.Admin);
        Assert.False(capabilities.Aliases);
        Assert.False(capabilities.PasswordChange);
        Assert.False(capabilities.ProfileEditing);
        Assert.False(capabilities.StrictIdentities);
        _accountInfo.Verify(a => a.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCapabilities_QuotaFollowsSupportsQuota()
    {
        _imapSession.SetupGet(s => s.SupportsQuota).Returns(false);

        var result = await CreateController().GetCapabilities(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(Assert.IsType<CapabilitiesResponse>(ok.Value).Quota);
    }

    [Fact]
    public async Task GetCapabilities_RulesFollowsTheProbe()
    {
        _sieveProbe.Setup(p => p.IsAvailableAsync(_sieve.Host, _sieve.Port, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);

        var result = await CreateController().GetCapabilities(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(Assert.IsType<CapabilitiesResponse>(ok.Value).Rules);
    }

    [Fact]
    public async Task GetCapabilities_WhenCredentialsUnavailable_Returns401WithEnvelope()
    {
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailAccountConnection>("credentials_unavailable"));

        var result = await CreateController().GetCapabilities(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ResultEnveloppe>(unauthorized.Value);
        Assert.Equal("credentials_unavailable", envelope.Message);
    }

    [Fact]
    public async Task GetCapabilities_WhenSessionFails_Returns502WithEnvelope()
    {
        _imapSessions.Setup(s => s.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

        var result = await CreateController().GetCapabilities(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(502, obj.StatusCode);
    }

    [Fact]
    public async Task Dav_FollowsWhetherAPublicAddressIsConfigured()
    {
        var withAddress = await GetCapabilitiesAsync(davPublicUrl: "https://api.mail.weesky.net");
        Assert.True(withAddress.Dav);

        var without = await GetCapabilitiesAsync(davPublicUrl: null);
        Assert.False(without.Dav);
    }
}
