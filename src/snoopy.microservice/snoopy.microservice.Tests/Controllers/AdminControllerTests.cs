using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class AdminControllerTests
    {
        private readonly Mock<IAdminRepository> _repo = new();
        private readonly Mock<IDovecotQuotaClient> _dovecot = new();

        private AdminController CreateController(bool isAdmin = true)
        {
            var controller = new AdminController(_repo.Object, _dovecot.Object);
            controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
            _repo.Setup(r => r.IsAdmin("john", "example.com")).Returns(isAdmin);
            return controller;
        }

        // ── GetUsers ───────────────────────────────────────────

        [Fact]
        public void GetUsers_WhenNotAdmin_Returns401()
        {
            var result = CreateController(isAdmin: false).GetUsers();
            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public void GetUsers_WhenAdmin_Returns200WithList()
        {
            var users = new[] { new AdminUserInfo { UserName = "alice" } };
            _repo.Setup(r => r.GetAllUsers()).Returns(users);
            var ok = Assert.IsType<OkObjectResult>(CreateController().GetUsers().Result);
            Assert.Same(users, ok.Value);
        }

        // ── CreateUser ────────────────────────────────────────

        [Fact]
        public void CreateUser_WhenNotAdmin_Returns401()
        {
            var result = CreateController(isAdmin: false)
                .CreateUser(new AdminUserRequest { UserName = "alice", Password = "pw" });
            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public void CreateUser_WhenPasswordNull_Returns400()
        {
            var obj = Assert.IsType<BadRequestObjectResult>(
                CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = null }).Result);
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void CreateUser_WhenPasswordEmpty_Returns400()
        {
            var obj = Assert.IsType<BadRequestObjectResult>(
                CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "" }).Result);
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void CreateUser_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.CreateUser(It.IsAny<AdminUserRequest>()))
                .Returns(Result.Failure<AdminUserInfo>("Duplicate user"));
            var obj = Assert.IsType<BadRequestObjectResult>(
                CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "pw" }).Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("Duplicate user", envelope.Message);
        }

        [Fact]
        public void CreateUser_WhenSuccess_Returns201WithUser()
        {
            var userInfo = new AdminUserInfo { UserName = "alice" };
            _repo.Setup(r => r.CreateUser(It.IsAny<AdminUserRequest>()))
                .Returns(Result.Success(userInfo));
            var obj = Assert.IsType<ObjectResult>(
                CreateController().CreateUser(new AdminUserRequest { UserName = "alice", Password = "pw" }).Result);
            Assert.Equal(201, obj.StatusCode);
            Assert.Same(userInfo, obj.Value);
        }

        // ── UpdateUser ────────────────────────────────────────

        [Fact]
        public void UpdateUser_WhenNotAdmin_Returns401()
        {
            var result = CreateController(isAdmin: false)
                .UpdateUser(1, new AdminUserRequest { UserName = "alice" });
            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public void UpdateUser_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.UpdateUser(It.IsAny<int>(), It.IsAny<AdminUserRequest>()))
                .Returns(Result.Failure<AdminUserInfo>("User not found"));
            var obj = Assert.IsType<BadRequestObjectResult>(
                CreateController().UpdateUser(1, new AdminUserRequest { UserName = "alice" }).Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("User not found", envelope.Message);
        }

        [Fact]
        public void UpdateUser_WhenSuccess_Returns200WithUser()
        {
            var userInfo = new AdminUserInfo { UserName = "alice" };
            _repo.Setup(r => r.UpdateUser(1, It.IsAny<AdminUserRequest>()))
                .Returns(Result.Success(userInfo));
            var ok = Assert.IsType<OkObjectResult>(
                CreateController().UpdateUser(1, new AdminUserRequest { UserName = "alice" }).Result);
            Assert.Same(userInfo, ok.Value);
        }

        // ── DeleteUser ────────────────────────────────────────

        [Fact]
        public void DeleteUser_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false).DeleteUser(1));
        }

        [Fact]
        public void DeleteUser_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.DeleteUser(It.IsAny<int>()))
                .Returns(Result.Failure("User not found"));
            var obj = Assert.IsType<ObjectResult>(CreateController().DeleteUser(1));
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void DeleteUser_WhenSuccess_Returns204()
        {
            _repo.Setup(r => r.DeleteUser(1)).Returns(Result.Success());
            var status = Assert.IsType<StatusCodeResult>(CreateController().DeleteUser(1));
            Assert.Equal(204, status.StatusCode);
        }

        // ── GetDomains ────────────────────────────────────────

        [Fact]
        public void GetDomains_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false).GetDomains().Result);
        }

        [Fact]
        public void GetDomains_WhenAdmin_Returns200WithList()
        {
            var domains = new[] { new Domain { Id = "WSY", Name = "weesky.be" } };
            _repo.Setup(r => r.GetAllDomains()).Returns(domains);
            var ok = Assert.IsType<OkObjectResult>(CreateController().GetDomains().Result);
            Assert.Same(domains, ok.Value);
        }

        // ── CreateDomain ──────────────────────────────────────

        [Fact]
        public void CreateDomain_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false)
                .CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" }).Result);
        }

        [Fact]
        public void CreateDomain_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.CreateDomain(It.IsAny<AdminDomainRequest>()))
                .Returns(Result.Failure<Domain>("Invalid id"));
            var obj = Assert.IsType<BadRequestObjectResult>(CreateController()
                .CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" }).Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("Invalid id", envelope.Message);
        }

        [Fact]
        public void CreateDomain_WhenSuccess_Returns201WithDomain()
        {
            var domain = new Domain { Id = "TST", Name = "test.com" };
            _repo.Setup(r => r.CreateDomain(It.IsAny<AdminDomainRequest>()))
                .Returns(Result.Success(domain));
            var obj = Assert.IsType<ObjectResult>(CreateController()
                .CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" }).Result);
            Assert.Equal(201, obj.StatusCode);
            Assert.Same(domain, obj.Value);
        }

        // ── UpdateDomain ──────────────────────────────────────

        [Fact]
        public void UpdateDomain_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false)
                .UpdateDomain("WSY", new AdminDomainRequest { Name = "new.com" }).Result);
        }

        [Fact]
        public void UpdateDomain_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.UpdateDomain(It.IsAny<string>(), It.IsAny<AdminDomainRequest>()))
                .Returns(Result.Failure<Domain>("Domain not found"));
            var obj = Assert.IsType<BadRequestObjectResult>(CreateController()
                .UpdateDomain("WSY", new AdminDomainRequest { Name = "new.com" }).Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("Domain not found", envelope.Message);
        }

        [Fact]
        public void UpdateDomain_WhenSuccess_Returns200WithDomain()
        {
            var domain = new Domain { Id = "WSY", Name = "new.com" };
            _repo.Setup(r => r.UpdateDomain("WSY", It.IsAny<AdminDomainRequest>()))
                .Returns(Result.Success(domain));
            var ok = Assert.IsType<OkObjectResult>(CreateController()
                .UpdateDomain("WSY", new AdminDomainRequest { Name = "new.com" }).Result);
            Assert.Same(domain, ok.Value);
        }

        // ── DeleteDomain ──────────────────────────────────────

        [Fact]
        public void DeleteDomain_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false).DeleteDomain("WSY"));
        }

        [Fact]
        public void DeleteDomain_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.DeleteDomain(It.IsAny<string>()))
                .Returns(Result.Failure("Domain has users"));
            var obj = Assert.IsType<ObjectResult>(CreateController().DeleteDomain("WSY"));
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void DeleteDomain_WhenSuccess_Returns204()
        {
            _repo.Setup(r => r.DeleteDomain("WSY")).Returns(Result.Success());
            var status = Assert.IsType<StatusCodeResult>(CreateController().DeleteDomain("WSY"));
            Assert.Equal(204, status.StatusCode);
        }

        // ── GetUserQuota ──────────────────────────────────────

        [Fact]
        public async Task GetUserQuota_WhenNotAdmin_Returns401()
        {
            var result = await CreateController(isAdmin: false).GetUserQuota(1, CancellationToken.None);
            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public async Task GetUserQuota_WhenUserNotFound_Returns400()
        {
            _repo.Setup(r => r.GetAllUsers()).Returns(Array.Empty<AdminUserInfo>());
            var result = await CreateController().GetUserQuota(1, CancellationToken.None);
            var obj = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public async Task GetUserQuota_WhenDovecotFails_Returns502()
        {
            _repo.Setup(r => r.GetAllUsers()).Returns(
                new[] { new AdminUserInfo { Id = 1, UserName = "alice", DomainName = "weesky.be" } });
            _dovecot.Setup(d => d.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<Quota>("Unreachable"));
            var result = await CreateController().GetUserQuota(1, CancellationToken.None);
            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(502, status.StatusCode);
        }

        [Fact]
        public async Task GetUserQuota_WhenSuccess_Returns200WithQuota()
        {
            var quota = new Quota { StorageBytesUsed = 1024 };
            _repo.Setup(r => r.GetAllUsers()).Returns(
                new[] { new AdminUserInfo { Id = 1, UserName = "alice", DomainName = "weesky.be" } });
            _dovecot.Setup(d => d.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(quota));
            var result = await CreateController().GetUserQuota(1, CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(quota, ok.Value);
        }

        [Fact]
        public async Task GetUserQuota_CallsDovecotWithCorrectEmail()
        {
            _repo.Setup(r => r.GetAllUsers()).Returns(
                new[] { new AdminUserInfo { Id = 1, UserName = "alice", DomainName = "weesky.be" } });
            _dovecot.Setup(d => d.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<Quota>("err"));
            await CreateController().GetUserQuota(1, CancellationToken.None);
            _dovecot.Verify(d => d.GetQuotaAsync(
                It.Is<User>(u => u.Email == "alice@weesky.be"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        // ── GetVirtualDomains ─────────────────────────────────

        [Fact]
        public void GetVirtualDomains_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false).GetVirtualDomains().Result);
        }

        [Fact]
        public void GetVirtualDomains_WhenAdmin_Returns200WithList()
        {
            var virtualDomains = new[] { new VirtualDomainInfo { DomainId = "EXT", DomainName = "extra.com", Owners = new() } };
            _repo.Setup(r => r.GetAllVirtualDomains()).Returns(virtualDomains);
            var ok = Assert.IsType<OkObjectResult>(CreateController().GetVirtualDomains().Result);
            Assert.Same(virtualDomains, ok.Value);
        }

        // ── AddVirtualDomainOwner ─────────────────────────────

        [Fact]
        public void AddVirtualDomainOwner_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false)
                .AddVirtualDomainOwner("EXT", new AdminVirtualDomainOwnerRequest { UserId = 1 }).Result);
        }

        [Fact]
        public void AddVirtualDomainOwner_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.AddVirtualDomainOwner(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(Result.Failure<VirtualDomainInfo>("User not found"));
            var obj = Assert.IsType<BadRequestObjectResult>(CreateController()
                .AddVirtualDomainOwner("EXT", new AdminVirtualDomainOwnerRequest { UserId = 1 }).Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("User not found", envelope.Message);
        }

        [Fact]
        public void AddVirtualDomainOwner_WhenSuccess_Returns200WithVirtualDomain()
        {
            var info = new VirtualDomainInfo { DomainId = "EXT", DomainName = "extra.com", Owners = new() { new OwnerInfo { OwnerId = 1, OwnerEmail = "alice@weesky.be" } } };
            _repo.Setup(r => r.AddVirtualDomainOwner("EXT", 1)).Returns(Result.Success(info));
            var ok = Assert.IsType<OkObjectResult>(CreateController()
                .AddVirtualDomainOwner("EXT", new AdminVirtualDomainOwnerRequest { UserId = 1 }).Result);
            Assert.Same(info, ok.Value);
        }

        // ── RemoveVirtualDomainOwner ──────────────────────────

        [Fact]
        public void RemoveVirtualDomainOwner_WhenNotAdmin_Returns401()
        {
            Assert.IsType<UnauthorizedResult>(CreateController(isAdmin: false).RemoveVirtualDomainOwner("EXT", 1));
        }

        [Fact]
        public void RemoveVirtualDomainOwner_WhenRepositoryFails_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.RemoveVirtualDomainOwner(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(Result.Failure("Owner not found"));
            var obj = Assert.IsType<ObjectResult>(CreateController().RemoveVirtualDomainOwner("EXT", 1));
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void RemoveVirtualDomainOwner_WhenSuccess_Returns204()
        {
            _repo.Setup(r => r.RemoveVirtualDomainOwner("EXT", 1)).Returns(Result.Success());
            var status = Assert.IsType<StatusCodeResult>(CreateController().RemoveVirtualDomainOwner("EXT", 1));
            Assert.Equal(204, status.StatusCode);
        }
    }
}
