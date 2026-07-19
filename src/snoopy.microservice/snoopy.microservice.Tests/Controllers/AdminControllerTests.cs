using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class AdminControllerTests
{
    private readonly Mock<IAdminRepository> _repo = new();
    private readonly Mock<IDovecotQuotaClient> _dovecot = new();

    private AdminController CreateController()
    {
        var controller = new AdminController(_repo.Object, _dovecot.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
        return controller;
    }

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
        _repo.Setup(r => r.GetAllUsersAsync()).ReturnsAsync(users);
        var ok = Assert.IsType<OkObjectResult>((await CreateController().GetUsers()).Result);
        Assert.Same(users, ok.Value);
    }

    // ── CreateUser ────────────────────────────────────────

    [Fact]
    public async Task CreateUser_WhenPasswordNull_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = null })).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WhenPasswordEmpty_Returns400()
    {
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "" })).Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.CreateUserAsync(It.IsAny<AdminUserRequest>()))
            .ReturnsAsync(Result.Failure<AdminUserInfo>("Duplicate user"));
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "pw" })).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Duplicate user", envelope.Message);
    }

    [Fact]
    public async Task CreateUser_WhenSuccess_Returns201WithUser()
    {
        var userInfo = new AdminUserInfo { UserName = "alice" };
        _repo.Setup(r => r.CreateUserAsync(It.IsAny<AdminUserRequest>()))
            .ReturnsAsync(Result.Success(userInfo));
        var obj = Assert.IsType<ObjectResult>(
            (await CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "pw" })).Result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Same(userInfo, obj.Value);
    }

    // ── UpdateUser ────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.UpdateUserAsync(It.IsAny<int>(), It.IsAny<AdminUserRequest>()))
            .ReturnsAsync(Result.Failure<AdminUserInfo>("User not found"));
        var obj = Assert.IsType<BadRequestObjectResult>(
            (await CreateController().UpdateUser(1, new AdminUserRequest { UserName = "alice" })).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("User not found", envelope.Message);
    }

    [Fact]
    public async Task UpdateUser_WhenSuccess_Returns200WithUser()
    {
        var userInfo = new AdminUserInfo { UserName = "alice" };
        _repo.Setup(r => r.UpdateUserAsync(1, It.IsAny<AdminUserRequest>()))
            .ReturnsAsync(Result.Success(userInfo));
        var ok = Assert.IsType<OkObjectResult>(
            (await CreateController().UpdateUser(1, new AdminUserRequest { UserName = "alice" })).Result);
        Assert.Same(userInfo, ok.Value);
    }

    // ── DeleteUser ────────────────────────────────────────

    [Fact]
    public async Task DeleteUser_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.DeleteUserAsync(It.IsAny<int>()))
            .ReturnsAsync(Result.Failure("User not found"));
        var obj = Assert.IsType<ObjectResult>(await CreateController().DeleteUser(1));
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.DeleteUserAsync(1)).ReturnsAsync(Result.Success());
        var status = Assert.IsType<StatusCodeResult>(await CreateController().DeleteUser(1));
        Assert.Equal(204, status.StatusCode);
    }

    // ── GetDomains ────────────────────────────────────────

    [Fact]
    public async Task GetDomains_Returns200WithList()
    {
        var domains = new[] { new Domain { Id = "WSY", Name = "weesky.be" } };
        _repo.Setup(r => r.GetAllDomainsAsync()).ReturnsAsync(domains);
        var ok = Assert.IsType<OkObjectResult>((await CreateController().GetDomains()).Result);
        Assert.Same(domains, ok.Value);
    }

    // ── CreateDomain ──────────────────────────────────────

    [Fact]
    public async Task CreateDomain_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.CreateDomainAsync(It.IsAny<AdminDomainRequest>()))
            .ReturnsAsync(Result.Failure<Domain>("Invalid id"));
        var obj = Assert.IsType<BadRequestObjectResult>((await CreateController()
            .CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" })).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Invalid id", envelope.Message);
    }

    [Fact]
    public async Task CreateDomain_WhenSuccess_Returns201WithDomain()
    {
        var domain = new Domain { Id = "TST", Name = "test.com" };
        _repo.Setup(r => r.CreateDomainAsync(It.IsAny<AdminDomainRequest>()))
            .ReturnsAsync(Result.Success(domain));
        var obj = Assert.IsType<ObjectResult>((await CreateController()
            .CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" })).Result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Same(domain, obj.Value);
    }

    // ── UpdateDomain ──────────────────────────────────────

    [Fact]
    public async Task UpdateDomain_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.UpdateDomainAsync(It.IsAny<string>(), It.IsAny<AdminDomainRequest>()))
            .ReturnsAsync(Result.Failure<Domain>("Domain not found"));
        var obj = Assert.IsType<BadRequestObjectResult>((await CreateController()
            .UpdateDomain("WSY", new AdminDomainRequest { Name = "new.com" })).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Domain not found", envelope.Message);
    }

    [Fact]
    public async Task UpdateDomain_WhenSuccess_Returns200WithDomain()
    {
        var domain = new Domain { Id = "WSY", Name = "new.com" };
        _repo.Setup(r => r.UpdateDomainAsync("WSY", It.IsAny<AdminDomainRequest>()))
            .ReturnsAsync(Result.Success(domain));
        var ok = Assert.IsType<OkObjectResult>((await CreateController()
            .UpdateDomain("WSY", new AdminDomainRequest { Name = "new.com" })).Result);
        Assert.Same(domain, ok.Value);
    }

    // ── DeleteDomain ──────────────────────────────────────

    [Fact]
    public async Task DeleteDomain_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.DeleteDomainAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Failure("Domain has users"));
        var obj = Assert.IsType<ObjectResult>(await CreateController().DeleteDomain("WSY"));
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task DeleteDomain_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.DeleteDomainAsync("WSY")).ReturnsAsync(Result.Success());
        var status = Assert.IsType<StatusCodeResult>(await CreateController().DeleteDomain("WSY"));
        Assert.Equal(204, status.StatusCode);
    }

    // ── GetUserQuota ──────────────────────────────────────

    [Fact]
    public async Task GetUserQuota_WhenUserNotFound_Returns400()
    {
        _repo.Setup(r => r.GetUserByIdAsync(1)).ReturnsAsync((AdminUserInfo?)null);
        var result = await CreateController().GetUserQuota(1, CancellationToken.None);
        var obj = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task GetUserQuota_WhenDovecotFails_Returns502WithEnvelope()
    {
        _repo.Setup(r => r.GetUserByIdAsync(1)).ReturnsAsync(
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
        _repo.Setup(r => r.GetUserByIdAsync(1)).ReturnsAsync(
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
        _repo.Setup(r => r.GetUserByIdAsync(1)).ReturnsAsync(
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
        _repo.Setup(r => r.GetAllVirtualDomainsAsync()).ReturnsAsync(virtualDomains);
        var ok = Assert.IsType<OkObjectResult>((await CreateController().GetVirtualDomains()).Result);
        Assert.Same(virtualDomains, ok.Value);
    }

    // ── AddVirtualDomainOwner ─────────────────────────────

    [Fact]
    public async Task AddVirtualDomainOwner_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.AddVirtualDomainOwnerAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Result.Failure<VirtualDomainInfo>("User not found"));
        var obj = Assert.IsType<BadRequestObjectResult>((await CreateController()
            .AddVirtualDomainOwner("EXT", new AdminVirtualDomainOwnerRequest { UserId = 1 })).Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("User not found", envelope.Message);
    }

    [Fact]
    public async Task AddVirtualDomainOwner_WhenSuccess_Returns200WithVirtualDomain()
    {
        var info = new VirtualDomainInfo { DomainId = "EXT", DomainName = "extra.com", Owners = new() { new OwnerInfo { OwnerId = 1, OwnerEmail = "alice@weesky.be" } } };
        _repo.Setup(r => r.AddVirtualDomainOwnerAsync("EXT", 1)).ReturnsAsync(Result.Success(info));
        var ok = Assert.IsType<OkObjectResult>((await CreateController()
            .AddVirtualDomainOwner("EXT", new AdminVirtualDomainOwnerRequest { UserId = 1 })).Result);
        Assert.Same(info, ok.Value);
    }

    // ── RemoveVirtualDomainOwner ──────────────────────────

    [Fact]
    public async Task RemoveVirtualDomainOwner_WhenRepositoryFails_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.RemoveVirtualDomainOwnerAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Result.Failure("Owner not found"));
        var obj = Assert.IsType<ObjectResult>(await CreateController().RemoveVirtualDomainOwner("EXT", 1));
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task RemoveVirtualDomainOwner_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.RemoveVirtualDomainOwnerAsync("EXT", 1)).ReturnsAsync(Result.Success());
        var status = Assert.IsType<StatusCodeResult>(await CreateController().RemoveVirtualDomainOwner("EXT", 1));
        Assert.Equal(204, status.StatusCode);
    }
}
