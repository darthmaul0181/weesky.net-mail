using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class AdminControllerTests
{
    private readonly Mock<IAdminRepository> _repo = new();
    private readonly Mock<IDovecotQuotaClient> _dovecot = new();
    private readonly Mock<IExternalDomainStore> _externalDomains = new();

    private AdminController CreateController(bool allowCleartext = false)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { AllowCleartext = allowCleartext });

        var controller = new AdminController(
            _repo.Object, _dovecot.Object, _externalDomains.Object, monitor.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
        return controller;
    }

    private static ExternalDomain Domain(
        Guid? id = null, string name = "Gmail",
        string imapHost = "imap.gmail.com", int imapPort = 993, string imapSecurity = "SslOnConnect",
        string smtpHost = "smtp.gmail.com", int smtpPort = 587, string smtpSecurity = "StartTls",
        string? sieveHost = null, int? sievePort = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        ImapHost = imapHost,
        ImapPort = imapPort,
        ImapSecurity = imapSecurity,
        SmtpHost = smtpHost,
        SmtpPort = smtpPort,
        SmtpSecurity = smtpSecurity,
        SieveHost = sieveHost,
        SievePort = sievePort
    };

    private static ExternalDomainRequest ValidRequest(
        string name = "Gmail", string imapHost = "imap.gmail.com", int imapPort = 993, string imapSecurity = "SslOnConnect",
        string smtpHost = "smtp.gmail.com", int smtpPort = 587, string smtpSecurity = "StartTls",
        string? sieveHost = null, int? sievePort = null) =>
        new(name, imapHost, imapPort, imapSecurity, smtpHost, smtpPort, smtpSecurity, sieveHost, sievePort);

    // ── Authorization ─────────────────────────────────────

    [Fact]
    public void Controller_IsProtectedByAdminPolicy()
    {
        var attribute = typeof(AdminController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(AdminRequirement.PolicyName, attribute.Policy);
    }

    // ── GetUsers ───────────────────────────────────────────

    [Fact]
    public async Task GetUsers_Returns200WithList()
    {
        var users = new[] { new AdminUserInfo { UserName = "alice" } };
        _repo.Setup(r => r.GetAllUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(users);
        var ok = Assert.IsType<OkObjectResult>((await CreateController().GetUsers(CancellationToken.None)).Result);
        Assert.Same(users, ok.Value);
    }

    // ── CreateUser ────────────────────────────────────────

    [Fact]
    public async Task CreateUser_WhenPasswordNull_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = null }, CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WhenPasswordEmpty_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "" }, CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.CreateUserAsync(It.IsAny<AdminUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AdminUserInfo>("Duplicate user"));
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "pw" }, CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Duplicate user", envelope.Message);
    }

    [Fact]
    public async Task CreateUser_WhenSuccess_Returns201WithUser()
    {
        var userInfo = new AdminUserInfo { UserName = "alice" };
        _repo.Setup(r => r.CreateUserAsync(It.IsAny<AdminUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(userInfo));
        var obj = Assert.IsType<ObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "pw" }, CancellationToken.None)).Result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Same(userInfo, obj.Value);
    }

    // ── UpdateUser ────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.UpdateUserAsync(It.IsAny<int>(), It.IsAny<AdminUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AdminUserInfo>("User not found"));
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().UpdateUser(1, new AdminUserRequest { UserName = "alice" }, CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("User not found", envelope.Message);
    }

    [Fact]
    public async Task UpdateUser_WhenSuccess_Returns200WithUser()
    {
        var userInfo = new AdminUserInfo { UserName = "alice" };
        _repo.Setup(r => r.UpdateUserAsync(1, It.IsAny<AdminUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(userInfo));
        var ok = Assert.IsType<OkObjectResult>(
            (await CreateController().UpdateUser(1, new AdminUserRequest { UserName = "alice" }, CancellationToken.None)).Result);
        Assert.Same(userInfo, ok.Value);
    }

    // ── DeleteUser ────────────────────────────────────────

    [Fact]
    public async Task DeleteUser_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.DeleteUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("User not found"));
        var obj = Assert.IsType<ObjectResult>(await CreateController().DeleteUser(1, CancellationToken.None));
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.DeleteUserAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var status = Assert.IsType<StatusCodeResult>(await CreateController().DeleteUser(1, CancellationToken.None));
        Assert.Equal(204, status.StatusCode);
    }

    // ── GetDomains ────────────────────────────────────────

    [Fact]
    public async Task GetDomains_Returns200WithList()
    {
        var domains = new[] { new Domain { Id = "WSY", Name = "weesky.be" } };
        _repo.Setup(r => r.GetAllDomainsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(domains);
        var ok = Assert.IsType<OkObjectResult>((await CreateController().GetDomains(CancellationToken.None)).Result);
        Assert.Same(domains, ok.Value);
    }

    // ── CreateDomain ──────────────────────────────────────

    [Fact]
    public async Task CreateDomain_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.CreateDomainAsync(It.IsAny<AdminDomainRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Domain>("Invalid id"));
        var obj = Assert.IsType<BadRequestObjectResult>((await CreateController()
            .CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" }, CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Invalid id", envelope.Message);
    }

    [Fact]
    public async Task CreateDomain_WhenSuccess_Returns201WithDomain()
    {
        var domain = new Domain { Id = "TST", Name = "test.com" };
        _repo.Setup(r => r.CreateDomainAsync(It.IsAny<AdminDomainRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(domain));
        var obj = Assert.IsType<ObjectResult>((await CreateController()
            .CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" }, CancellationToken.None)).Result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Same(domain, obj.Value);
    }

    // ── UpdateDomain ──────────────────────────────────────

    [Fact]
    public async Task UpdateDomain_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.UpdateDomainAsync(It.IsAny<string>(), It.IsAny<AdminDomainRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Domain>("Domain not found"));
        var obj = Assert.IsType<BadRequestObjectResult>((await CreateController()
            .UpdateDomain("WSY", new AdminDomainRequest { Name = "new.com" }, CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Domain not found", envelope.Message);
    }

    [Fact]
    public async Task UpdateDomain_WhenSuccess_Returns200WithDomain()
    {
        var domain = new Domain { Id = "WSY", Name = "new.com" };
        _repo.Setup(r => r.UpdateDomainAsync("WSY", It.IsAny<AdminDomainRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(domain));
        var ok = Assert.IsType<OkObjectResult>((await CreateController()
            .UpdateDomain("WSY", new AdminDomainRequest { Name = "new.com" }, CancellationToken.None)).Result);
        Assert.Same(domain, ok.Value);
    }

    // ── DeleteDomain ──────────────────────────────────────

    [Fact]
    public async Task DeleteDomain_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.DeleteDomainAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Domain has users"));
        var obj = Assert.IsType<ObjectResult>(await CreateController().DeleteDomain("WSY", false, CancellationToken.None));
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task DeleteDomain_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.DeleteDomainAsync("WSY", It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var status = Assert.IsType<StatusCodeResult>(await CreateController().DeleteDomain("WSY", false, CancellationToken.None));
        Assert.Equal(204, status.StatusCode);
    }

    // The acknowledgement is the whole point of the query parameter: dropped on the way through,
    // the confirmation the user answered would never reach the guard it was answering.
    [Fact]
    public async Task DeleteDomain_PassesTheAliasAcknowledgementThrough()
    {
        _repo.Setup(r => r.DeleteDomainAsync("WSY", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var status = Assert.IsType<StatusCodeResult>(
            await CreateController().DeleteDomain("WSY", true, CancellationToken.None));

        Assert.Equal(204, status.StatusCode);
        _repo.Verify(r => r.DeleteDomainAsync("WSY", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetUserQuota ──────────────────────────────────────

    [Fact]
    public async Task GetUserQuota_WhenUserNotFound_Returns400()
    {
        _repo.Setup(r => r.GetUserByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((AdminUserInfo?)null);
        var result = await CreateController().GetUserQuota(1, CancellationToken.None);
        var obj = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task GetUserQuota_WhenDovecotFails_Returns502WithEnvelope()
    {
        _repo.Setup(r => r.GetUserByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AdminUserInfo { Id = 1, UserName = "alice", DomainName = "weesky.be" });
        _dovecot.Setup(d => d.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Quota>("Unreachable"));
        var result = await CreateController().GetUserQuota(1, CancellationToken.None);
        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(502, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Unreachable", envelope.Message);
    }

    [Fact]
    public async Task GetUserQuota_WhenSuccess_Returns200WithQuota()
    {
        var quota = new Quota { StorageBytesUsed = 1024 };
        _repo.Setup(r => r.GetUserByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AdminUserInfo { Id = 1, UserName = "alice", DomainName = "weesky.be" });
        _dovecot.Setup(d => d.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(quota));
        var result = await CreateController().GetUserQuota(1, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(quota, ok.Value);
    }

    [Fact]
    public async Task GetUserQuota_CallsDovecotWithCorrectEmail()
    {
        _repo.Setup(r => r.GetUserByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AdminUserInfo { Id = 1, UserName = "alice", DomainName = "weesky.be" });
        _dovecot.Setup(d => d.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Quota>("err"));
        await CreateController().GetUserQuota(1, CancellationToken.None);
        _dovecot.Verify(d => d.GetQuotaAsync(
            It.Is<User>(u => u.Email == "alice@weesky.be"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetVirtualDomains ─────────────────────────────────

    [Fact]
    public async Task GetVirtualDomains_Returns200WithList()
    {
        var virtualDomains = new[] { new VirtualDomainInfo { DomainId = "EXT", DomainName = "extra.com", Owners = new() } };
        _repo.Setup(r => r.GetAllVirtualDomainsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(virtualDomains);
        var ok = Assert.IsType<OkObjectResult>((await CreateController().GetVirtualDomains(CancellationToken.None)).Result);
        Assert.Same(virtualDomains, ok.Value);
    }

    // ── AddVirtualDomainOwner ─────────────────────────────

    [Fact]
    public async Task AddVirtualDomainOwner_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.AddVirtualDomainOwnerAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<VirtualDomainInfo>("User not found"));
        var obj = Assert.IsType<BadRequestObjectResult>((await CreateController()
            .AddVirtualDomainOwner("EXT", new AdminVirtualDomainOwnerRequest { UserId = 1 }, CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("User not found", envelope.Message);
    }

    [Fact]
    public async Task AddVirtualDomainOwner_WhenSuccess_Returns200WithVirtualDomain()
    {
        var info = new VirtualDomainInfo { DomainId = "EXT", DomainName = "extra.com", Owners = new() { new OwnerInfo { OwnerId = 1, OwnerEmail = "alice@weesky.be" } } };
        _repo.Setup(r => r.AddVirtualDomainOwnerAsync("EXT", 1, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(info));
        var ok = Assert.IsType<OkObjectResult>((await CreateController()
            .AddVirtualDomainOwner("EXT", new AdminVirtualDomainOwnerRequest { UserId = 1 }, CancellationToken.None)).Result);
        Assert.Same(info, ok.Value);
    }

    // ── RemoveVirtualDomainOwner ──────────────────────────

    [Fact]
    public async Task RemoveVirtualDomainOwner_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.RemoveVirtualDomainOwnerAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Owner not found"));
        var obj = Assert.IsType<ObjectResult>(await CreateController().RemoveVirtualDomainOwner("EXT", 1, CancellationToken.None));
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task RemoveVirtualDomainOwner_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.RemoveVirtualDomainOwnerAsync("EXT", 1, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var status = Assert.IsType<StatusCodeResult>(await CreateController().RemoveVirtualDomainOwner("EXT", 1, CancellationToken.None));
        Assert.Equal(204, status.StatusCode);
    }

    // ── GetExternalDomains ─────────────────────────────────

    [Fact]
    public async Task GetExternalDomains_Returns200WithList()
    {
        var domain = Domain();
        _externalDomains.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { domain });

        var ok = Assert.IsType<OkObjectResult>(
            (await CreateController().GetExternalDomains(CancellationToken.None)).Result);

        var list = Assert.IsAssignableFrom<IEnumerable<ExternalDomainResponse>>(ok.Value).ToList();
        var response = Assert.Single(list);
        Assert.Equal(domain.Id, response.Id);
        Assert.Equal(domain.Name, response.Name);
        Assert.Equal(domain.ImapHost, response.ImapHost);
        Assert.Equal(domain.SmtpSecurity, response.SmtpSecurity);
    }

    // ── CreateExternalDomain ───────────────────────────────

    [Fact]
    public async Task CreateExternalDomain_WhenValid_Returns200WithDomain()
    {
        var created = Domain();
        _externalDomains.Setup(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(created));

        var ok = Assert.IsType<OkObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(), CancellationToken.None)).Result);

        var response = Assert.IsType<ExternalDomainResponse>(ok.Value);
        Assert.Equal(created.Id, response.Id);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenStoreFails_Returns400WithEnvelope()
    {
        _externalDomains.Setup(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ExternalDomain>(ExternalDomainStore.NameTaken));

        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(), CancellationToken.None)).Result);

        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal(ExternalDomainStore.NameTaken, envelope.Message);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenNameEmpty_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(name: ""), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        _externalDomains.Verify(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Saving None while the resolver refuses it would store a row that answers 404 on every use.
    [Theory]
    [InlineData("None", "StartTls")]
    [InlineData("SslOnConnect", "None")]
    public async Task CreateExternalDomain_WhenCleartextWithoutTheOptIn_Returns400(string imap, string smtp)
    {
        var request = ValidRequest(imapSecurity: imap, smtpSecurity: smtp);

        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(request, CancellationToken.None)).Result);

        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Contains("Mail:AllowCleartext", envelope.Message);
        _externalDomains.Verify(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenCleartextWithTheOptIn_IsAccepted()
    {
        _externalDomains.Setup(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Domain()));
        var request = ValidRequest(imapSecurity: "None", smtpSecurity: "None");

        var result = await CreateController(allowCleartext: true).CreateExternalDomain(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenSecurityUnknown_Returns400WhateverTheOptIn()
    {
        var request = ValidRequest(imapSecurity: "Tls");

        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController(allowCleartext: true).CreateExternalDomain(request, CancellationToken.None)).Result);

        Assert.Equal(400, obj.StatusCode);
        _externalDomains.Verify(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenNameTooLong_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(name: new string('a', 101)), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenImapHostEmpty_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(imapHost: ""), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenImapHostNotAHostname_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(imapHost: "http://evil.com/"), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenImapHostTooLong_Returns400()
    {
        var host = new string('a', 250) + ".com";
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(imapHost: host), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenImapPortZero_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(imapPort: 0), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenSmtpPortTooLarge_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(smtpPort: 65536), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenImapSecurityUnknown_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(imapSecurity: "Bogus"), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    /// <summary>Enum.TryParse is case-insensitive-averse only by convention; the resolver used to
    /// accept this and brick the domain. Validation must refuse it outright, never normalise it.</summary>
    [Fact]
    public async Task CreateExternalDomain_WhenSecurityLowercase_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(imapSecurity: "starttls"), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        _externalDomains.Verify(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Enum.TryParse also accepts the underlying numeric value; "3" must be refused too.</summary>
    [Fact]
    public async Task CreateExternalDomain_WhenSecurityNumeric_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(ValidRequest(smtpSecurity: "3"), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
        _externalDomains.Verify(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenSieveHostWithoutPort_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(
                ValidRequest(sieveHost: "sieve.gmail.com", sievePort: null), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenSievePortWithoutHost_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(
                ValidRequest(sieveHost: null, sievePort: 4190), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenSievePortOutOfRange_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateExternalDomain(
                ValidRequest(sieveHost: "sieve.gmail.com", sievePort: 0), CancellationToken.None)).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateExternalDomain_WhenSieveBothPresent_Succeeds()
    {
        _externalDomains.Setup(s => s.CreateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Domain(sieveHost: "sieve.gmail.com", sievePort: 4190)));

        var ok = Assert.IsType<OkObjectResult>((await CreateController().CreateExternalDomain(
            ValidRequest(sieveHost: "sieve.gmail.com", sievePort: 4190), CancellationToken.None)).Result);

        Assert.IsType<ExternalDomainResponse>(ok.Value);
    }

    // ── UpdateExternalDomain ───────────────────────────────

    [Fact]
    public async Task UpdateExternalDomain_WhenValid_Returns204()
    {
        var id = Guid.NewGuid();
        _externalDomains.Setup(s => s.UpdateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().UpdateExternalDomain(id, ValidRequest(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateExternalDomain_WhenInvalid_Returns400()
    {
        var result = await CreateController().UpdateExternalDomain(
            Guid.NewGuid(), ValidRequest(imapPort: 0), CancellationToken.None);

        var obj = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
        _externalDomains.Verify(s => s.UpdateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateExternalDomain_WhenNotFound_Returns404()
    {
        _externalDomains.Setup(s => s.UpdateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ExternalDomainStore.NotFound));

        var result = await CreateController().UpdateExternalDomain(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        var obj = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateExternalDomain_WhenNameTaken_Returns400WithEnvelope()
    {
        _externalDomains.Setup(s => s.UpdateAsync(It.IsAny<ExternalDomain>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ExternalDomainStore.NameTaken));

        var result = await CreateController().UpdateExternalDomain(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        var obj = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal(ExternalDomainStore.NameTaken, envelope.Message);
    }

    // ── DeleteExternalDomain ───────────────────────────────

    [Fact]
    public async Task DeleteExternalDomain_WhenSuccess_Returns204()
    {
        _externalDomains.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().DeleteExternalDomain(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteExternalDomain_WhenInUse_Returns400WithEnvelope()
    {
        _externalDomains.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ExternalDomainStore.InUse));

        var result = await CreateController().DeleteExternalDomain(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Contains("connected", envelope.Message, StringComparison.OrdinalIgnoreCase);
    }
}
