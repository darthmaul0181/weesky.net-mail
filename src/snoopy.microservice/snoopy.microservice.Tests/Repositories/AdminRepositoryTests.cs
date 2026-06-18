using CSharpFunctionalExtensions;
using Moq;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class AdminRepositoryTests
    {
        private static TestDbContext CreateContext() => new(Guid.NewGuid().ToString());

        private static Mock<IDoveadmClient> CreateDoveadm()
        {
            var mock = new Mock<IDoveadmClient>();
            mock.Setup(d => d.FlushAuthCacheAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());
            return mock;
        }

        private static AdminRepository CreateRepo(TestDbContext ctx, Mock<IDoveadmClient>? doveadm = null) =>
            new(ctx, (doveadm ?? CreateDoveadm()).Object);

        private static MailDomain AddDomain(TestDbContext ctx, string id = "WSY", string name = "weesky.be")
        {
            var d = new MailDomain { Id = id, Name = name };
            ctx.Domains.Add(d);
            ctx.SaveChanges();
            return d;
        }

        private static MailUser AddUser(TestDbContext ctx, string name, string domainId,
            ActiveState admin = ActiveState.N, ActiveState active = ActiveState.Y,
            int quotaMb = 1024, string fullName = "")
        {
            var u = new MailUser
            {
                Name = name,
                DomainId = domainId,
                Password = "pw",
                FullName = fullName,
                QuotaMb = quotaMb,
                Active = active,
                Admin = admin,
                LastUpdate = DateTime.UtcNow
            };
            ctx.Users.Add(u);
            ctx.SaveChanges();
            return u;
        }

        // ── IsAdmin ───────────────────────────────────────────

        [Fact]
        public async Task IsAdmin_WhenDomainNotFound_ReturnsFalse()
        {
            using var ctx = CreateContext();
            Assert.False(await CreateRepo(ctx).IsAdminAsync("alice", "unknown.com"));
        }

        [Fact]
        public async Task IsAdmin_WhenUserNotInDomain_ReturnsFalse()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            Assert.False(await CreateRepo(ctx).IsAdminAsync("nobody", "weesky.be"));
        }

        [Fact]
        public async Task IsAdmin_WhenUserAdminFlagIsN_ReturnsFalse()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY", admin: ActiveState.N);
            Assert.False(await CreateRepo(ctx).IsAdminAsync("alice", "weesky.be"));
        }

        [Fact]
        public async Task IsAdmin_WhenUserAdminFlagIsY_ReturnsTrue()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
            Assert.True(await CreateRepo(ctx).IsAdminAsync("alice", "weesky.be"));
        }

        [Fact]
        public async Task IsAdmin_IsCaseInsensitiveForUsername()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
            Assert.True(await CreateRepo(ctx).IsAdminAsync("ALICE", "weesky.be"));
        }

        // ── GetAllUsers ───────────────────────────────────────

        [Fact]
        public async Task GetAllUsers_WithNoUsers_ReturnsEmpty()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            Assert.Empty(await CreateRepo(ctx).GetAllUsersAsync());
        }

        [Fact]
        public async Task GetAllUsers_ReturnsAllUsersWithDomainName()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            AddUser(ctx, "bob", "WSY");
            var users = (await CreateRepo(ctx).GetAllUsersAsync()).ToList();
            Assert.Equal(2, users.Count);
            Assert.All(users, u => Assert.Equal("weesky.be", u.DomainName));
        }

        [Fact]
        public async Task GetAllUsers_MapsActiveFlagCorrectly()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "active", "WSY", active: ActiveState.Y);
            AddUser(ctx, "inactive", "WSY", active: ActiveState.N);
            var users = (await CreateRepo(ctx).GetAllUsersAsync()).ToList();
            Assert.True(users.First(u => u.UserName == "active").Active);
            Assert.False(users.First(u => u.UserName == "inactive").Active);
        }

        [Fact]
        public async Task GetAllUsers_MapsAdminFlagCorrectly()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "superuser", "WSY", admin: ActiveState.Y);
            AddUser(ctx, "regular", "WSY", admin: ActiveState.N);
            var users = (await CreateRepo(ctx).GetAllUsersAsync()).ToList();
            Assert.True(users.First(u => u.UserName == "superuser").Admin);
            Assert.False(users.First(u => u.UserName == "regular").Admin);
        }

        [Fact]
        public async Task GetAllUsers_MapsLastLoginsWhenPresent()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            var ts = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            ctx.LastLogins.Add(new LastLogin { UserId = "alice@weesky.be", Service = "imap", LastAccess = ts });
            ctx.SaveChanges();

            var users = (await CreateRepo(ctx).GetAllUsersAsync()).ToList();
            var alice = users.Single(u => u.UserName == "alice");
            Assert.Single(alice.LastLogins);
            Assert.Equal("imap", alice.LastLogins[0].Service);
            Assert.Equal(new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc), alice.LastLogins[0].At);
        }

        [Fact]
        public async Task GetAllUsers_ReturnsEmptyLastLoginsWhenNone()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            var users = (await CreateRepo(ctx).GetAllUsersAsync()).ToList();
            Assert.Empty(users.Single().LastLogins);
        }

        [Fact]
        public async Task GetAllUsers_OrdersLastLoginsMostRecentFirst()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            var older = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var newer = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            ctx.LastLogins.Add(new LastLogin { UserId = "alice@weesky.be", Service = "lmtp", LastAccess = older });
            ctx.LastLogins.Add(new LastLogin { UserId = "alice@weesky.be", Service = "imap", LastAccess = newer });
            ctx.SaveChanges();

            var logins = (await CreateRepo(ctx).GetAllUsersAsync()).Single().LastLogins;
            Assert.Equal("imap", logins[0].Service);
            Assert.Equal("lmtp", logins[1].Service);
        }

        // ── GetUserById ───────────────────────────────────────

        [Fact]
        public async Task GetUserById_WhenNotFound_ReturnsNull()
        {
            using var ctx = CreateContext();
            Assert.Null(await CreateRepo(ctx).GetUserByIdAsync(999));
        }

        [Fact]
        public async Task GetUserById_WhenFound_ReturnsUserWithDomainName()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.Y, quotaMb: 2048, fullName: "Alice Smith");

            var info = await CreateRepo(ctx).GetUserByIdAsync(user.Id);

            Assert.NotNull(info);
            Assert.Equal(user.Id, info.Id);
            Assert.Equal("alice", info.UserName);
            Assert.Equal("weesky.be", info.DomainName);
            Assert.Equal("Alice Smith", info.FullName);
            Assert.Equal(2048, info.QuotaMb);
            Assert.True(info.Admin);
        }

        // ── CreateUser ────────────────────────────────────────

        [Fact]
        public async Task CreateUser_WhenUsernameBlank_ReturnsFailure()
        {
            using var ctx = CreateContext();
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "   ", DomainId = "WSY", Password = "password123" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task CreateUser_WhenPasswordNull_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = null });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task CreateUser_WhenPasswordEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task CreateUser_WhenPasswordTooShort_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "short77" });
            Assert.True(result.IsFailure);
            Assert.Equal("Password must contain at least 8 characters", result.Error);
        }

        [Fact]
        public async Task CreateUser_WhenDomainNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "ZZZ", Password = "password123" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task CreateUser_WhenUsernameAlreadyExistsInDomain_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "password123" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task CreateUser_DuplicateCheckIsCaseInsensitive()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "ALICE", DomainId = "WSY", Password = "password123" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task CreateUser_WithValidRequest_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var result = await CreateRepo(ctx).CreateUserAsync(new AdminUserRequest
            {
                UserName = "alice",
                DomainId = "WSY",
                Password = "secret123",
                FullName = "Alice Smith",
                QuotaMb = 2048,
                Active = true,
                Admin = false
            });
            Assert.True(result.IsSuccess);
            Assert.Equal("alice", result.Value.UserName);
            Assert.Equal("weesky.be", result.Value.DomainName);
            Assert.Equal(2048, result.Value.QuotaMb);
            Assert.True(result.Value.Active);
            Assert.False(result.Value.Admin);
        }

        [Fact]
        public async Task CreateUser_NormalisesUsernameLowercase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "ALICE", DomainId = "WSY", Password = "password123" });
            Assert.Equal("alice", result.Value.UserName);
        }

        [Fact]
        public async Task CreateUser_StoresPasswordAsPlaintext()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "mysecret" });
            Assert.Equal("mysecret", ctx.Users.First(u => u.Name == "alice").Password);
        }

        [Fact]
        public async Task CreateUser_AssignsAdminFlag()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = await CreateRepo(ctx).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "password123", Admin = true });
            Assert.True(result.Value.Admin);
            Assert.Equal(ActiveState.Y, ctx.Users.First(u => u.Name == "alice").Admin);
        }

        [Fact]
        public async Task CreateUser_DoesNotFlushAuthCache()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var doveadm = CreateDoveadm();

            await CreateRepo(ctx, doveadm).CreateUserAsync(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "password123" });

            doveadm.Verify(d => d.FlushAuthCacheAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── UpdateUser ────────────────────────────────────────

        [Fact]
        public async Task UpdateUser_WhenUserNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            var result = await CreateRepo(ctx).UpdateUserAsync(999,
                new AdminUserRequest { UserName = "x", QuotaMb = 1024 });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task UpdateUser_UpdatesFullName()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", fullName: "Old Name");
            var result = await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", FullName = "New Name", QuotaMb = 1024 });
            Assert.True(result.IsSuccess);
            Assert.Equal("New Name", result.Value.FullName);
        }

        [Fact]
        public async Task UpdateUser_UpdatesQuota()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", quotaMb: 1024);
            var result = await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 4096 });
            Assert.Equal(4096, result.Value.QuotaMb);
        }

        [Fact]
        public async Task UpdateUser_WhenPasswordNull_DoesNotChangePassword()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            var originalPw = ctx.Users.First(u => u.Id == user.Id).Password;
            await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", Password = null, QuotaMb = 1024 });
            Assert.Equal(originalPw, ctx.Users.First(u => u.Id == user.Id).Password);
        }

        [Fact]
        public async Task UpdateUser_WhenPasswordEmpty_DoesNotChangePassword()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            var originalPw = ctx.Users.First(u => u.Id == user.Id).Password;
            await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", Password = "", QuotaMb = 1024 });
            Assert.Equal(originalPw, ctx.Users.First(u => u.Id == user.Id).Password);
        }

        [Fact]
        public async Task UpdateUser_WhenPasswordTooShort_ReturnsFailureAndDoesNotChangePassword()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            var originalPw = ctx.Users.First(u => u.Id == user.Id).Password;
            var result = await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", Password = "short77", QuotaMb = 1024 });
            Assert.True(result.IsFailure);
            Assert.Equal("Password must contain at least 8 characters", result.Error);
            Assert.Equal(originalPw, ctx.Users.First(u => u.Id == user.Id).Password);
        }

        [Fact]
        public async Task UpdateUser_WhenPasswordProvided_UpdatesPasswordAsPlaintext()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", Password = "newpass123", QuotaMb = 1024 });
            Assert.Equal("newpass123", ctx.Users.First(u => u.Id == user.Id).Password);
        }

        [Fact]
        public async Task UpdateUser_UpdatesActiveFlag()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", active: ActiveState.Y);
            var result = await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 1024, Active = false });
            Assert.False(result.Value.Active);
        }

        [Fact]
        public async Task UpdateUser_UpdatesAdminFlag()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.N);
            var result = await CreateRepo(ctx).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 1024, Admin = true });
            Assert.True(result.Value.Admin);
        }

        [Fact]
        public async Task UpdateUser_OnSuccess_FlushesAuthCache()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            var doveadm = CreateDoveadm();

            await CreateRepo(ctx, doveadm).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 4096 });

            doveadm.Verify(d => d.FlushAuthCacheAsync("alice@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUser_WhenFlushFails_StillReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            var doveadm = CreateDoveadm();
            doveadm.Setup(d => d.FlushAuthCacheAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure("Dovecot unreachable"));

            var result = await CreateRepo(ctx, doveadm).UpdateUserAsync(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 4096 });

            Assert.True(result.IsSuccess);
        }

        // ── DeleteUser ────────────────────────────────────────

        [Fact]
        public async Task DeleteUser_WhenNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).DeleteUserAsync(999)).IsFailure);
        }

        [Fact]
        public async Task DeleteUser_WhenFound_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            Assert.True((await CreateRepo(ctx).DeleteUserAsync(user.Id)).IsSuccess);
        }

        [Fact]
        public async Task DeleteUser_RemovesUserFromDatabase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            await CreateRepo(ctx).DeleteUserAsync(user.Id);
            Assert.False(ctx.Users.Any(u => u.Id == user.Id));
        }

        [Fact]
        public async Task DeleteUser_OnSuccess_FlushesAuthCache()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            var doveadm = CreateDoveadm();

            await CreateRepo(ctx, doveadm).DeleteUserAsync(user.Id);

            doveadm.Verify(d => d.FlushAuthCacheAsync("alice@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_WhenUserNotFound_DoesNotFlushAuthCache()
        {
            using var ctx = CreateContext();
            var doveadm = CreateDoveadm();

            await CreateRepo(ctx, doveadm).DeleteUserAsync(999);

            doveadm.Verify(d => d.FlushAuthCacheAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── GetAllDomains ─────────────────────────────────────

        [Fact]
        public async Task GetAllDomains_WithNoDomains_ReturnsEmpty()
        {
            using var ctx = CreateContext();
            Assert.Empty(await CreateRepo(ctx).GetAllDomainsAsync());
        }

        [Fact]
        public async Task GetAllDomains_ReturnsAllDomains()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddDomain(ctx, "TST", "test.com");
            var domains = (await CreateRepo(ctx).GetAllDomainsAsync()).ToList();
            Assert.Equal(2, domains.Count);
            Assert.Contains(domains, d => d.Id == "WSY" && d.Name == "weesky.be");
            Assert.Contains(domains, d => d.Id == "TST" && d.Name == "test.com");
        }

        // ── CreateDomain ──────────────────────────────────────

        [Fact]
        public async Task CreateDomain_WhenIdEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).CreateDomainAsync(
                new AdminDomainRequest { Id = "", Name = "test.com" })).IsFailure);
        }

        [Fact]
        public async Task CreateDomain_WhenIdTooLong_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).CreateDomainAsync(
                new AdminDomainRequest { Id = "ABCD", Name = "test.com" })).IsFailure);
        }

        [Fact]
        public async Task CreateDomain_WhenNameEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).CreateDomainAsync(
                new AdminDomainRequest { Id = "TST", Name = "" })).IsFailure);
        }

        [Fact]
        public async Task CreateDomain_WhenIdAlreadyExists_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            Assert.True((await CreateRepo(ctx).CreateDomainAsync(
                new AdminDomainRequest { Id = "WSY", Name = "other.com" })).IsFailure);
        }

        [Fact]
        public async Task CreateDomain_WithValidRequest_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            var result = await CreateRepo(ctx).CreateDomainAsync(
                new AdminDomainRequest { Id = "TST", Name = "test.com" });
            Assert.True(result.IsSuccess);
            Assert.Equal("test.com", result.Value.Name);
        }

        [Fact]
        public async Task CreateDomain_NormalisesIdToUppercase()
        {
            using var ctx = CreateContext();
            var result = await CreateRepo(ctx).CreateDomainAsync(
                new AdminDomainRequest { Id = "tst", Name = "test.com" });
            Assert.Equal("TST", result.Value.Id);
        }

        [Fact]
        public async Task CreateDomain_PersistsDomainToDatabase()
        {
            using var ctx = CreateContext();
            await CreateRepo(ctx).CreateDomainAsync(new AdminDomainRequest { Id = "TST", Name = "test.com" });
            Assert.True(ctx.Domains.Any(d => d.Id == "TST"));
        }

        // ── UpdateDomain ──────────────────────────────────────

        [Fact]
        public async Task UpdateDomain_WhenNameEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).UpdateDomainAsync("WSY",
                new AdminDomainRequest { Name = "" })).IsFailure);
        }

        [Fact]
        public async Task UpdateDomain_WhenDomainNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).UpdateDomainAsync("ZZZ",
                new AdminDomainRequest { Name = "new.com" })).IsFailure);
        }

        [Fact]
        public async Task UpdateDomain_WithValidRequest_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var result = await CreateRepo(ctx).UpdateDomainAsync("WSY",
                new AdminDomainRequest { Name = "new.weesky.be" });
            Assert.True(result.IsSuccess);
            Assert.Equal("new.weesky.be", result.Value.Name);
        }

        [Fact]
        public async Task UpdateDomain_PersistsNameChangeToDatabase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            await CreateRepo(ctx).UpdateDomainAsync("WSY", new AdminDomainRequest { Name = "updated.be" });
            Assert.Equal("updated.be", ctx.Domains.First(d => d.Id == "WSY").Name);
        }

        // ── DeleteDomain ──────────────────────────────────────

        [Fact]
        public async Task DeleteDomain_WhenNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).DeleteDomainAsync("ZZZ")).IsFailure);
        }

        [Fact]
        public async Task DeleteDomain_WhenDomainHasUsers_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            Assert.True((await CreateRepo(ctx).DeleteDomainAsync("WSY")).IsFailure);
        }

        [Fact]
        public async Task DeleteDomain_WhenDomainEmpty_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            Assert.True((await CreateRepo(ctx).DeleteDomainAsync("WSY")).IsSuccess);
        }

        [Fact]
        public async Task DeleteDomain_RemovesDomainFromDatabase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            await CreateRepo(ctx).DeleteDomainAsync("WSY");
            Assert.False(ctx.Domains.Any(d => d.Id == "WSY"));
        }

        // ── GetAllVirtualDomains ──────────────────────────────────

        private static void AddOwnership(TestDbContext ctx, string domainId, int userId)
        {
            ctx.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });
            ctx.SaveChanges();
        }

        [Fact]
        public async Task GetAllVirtualDomains_WithNoDomains_ReturnsEmpty()
        {
            using var ctx = CreateContext();
            Assert.Empty(await CreateRepo(ctx).GetAllVirtualDomainsAsync());
        }

        [Fact]
        public async Task GetAllVirtualDomains_ExcludesPrimaryDomainWithNoOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            Assert.Empty(await CreateRepo(ctx).GetAllVirtualDomainsAsync());
        }

        [Fact]
        public async Task GetAllVirtualDomains_IncludesPrimaryDomainWhenInOwnershipsTable()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            AddOwnership(ctx, "WSY", alice.Id);
            var result = (await CreateRepo(ctx).GetAllVirtualDomainsAsync()).ToList();
            Assert.Single(result);
            Assert.Equal("WSY", result[0].DomainId);
            Assert.Contains(result[0].Owners, o => o.OwnerId == alice.Id);
        }

        [Fact]
        public async Task GetAllVirtualDomains_ReturnsAliasDomainWithNoOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "EXT", "extra.com");
            var result = (await CreateRepo(ctx).GetAllVirtualDomainsAsync()).ToList();
            Assert.Single(result);
            Assert.Equal("EXT", result[0].DomainId);
            Assert.Equal("extra.com", result[0].DomainName);
            Assert.Empty(result[0].Owners);
        }

        [Fact]
        public async Task GetAllVirtualDomains_ReturnsAliasDomainWithOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", user.Id);
            var result = (await CreateRepo(ctx).GetAllVirtualDomainsAsync()).ToList();
            Assert.Single(result);
            Assert.Equal("EXT", result[0].DomainId);
            Assert.Single(result[0].Owners);
            Assert.Equal(user.Id, result[0].Owners[0].OwnerId);
            Assert.Equal("alice@weesky.be", result[0].Owners[0].OwnerEmail);
        }

        [Fact]
        public async Task GetAllVirtualDomains_ReturnsAliasDomainWithMultipleOwners()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            var bob = AddUser(ctx, "bob", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            AddOwnership(ctx, "EXT", bob.Id);
            var result = (await CreateRepo(ctx).GetAllVirtualDomainsAsync()).ToList();
            Assert.Single(result);
            Assert.Equal(2, result[0].Owners.Count);
            Assert.Contains(result[0].Owners, o => o.OwnerEmail == "alice@weesky.be");
            Assert.Contains(result[0].Owners, o => o.OwnerEmail == "bob@weesky.be");
        }

        // ── AddVirtualDomainOwner ──────────────────────────────────────

        [Fact]
        public async Task AddVirtualDomainOwner_WhenDomainNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).AddVirtualDomainOwnerAsync("ZZZ", 1)).IsFailure);
        }

        [Fact]
        public async Task AddVirtualDomainOwner_WhenUserNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "EXT", "extra.com");
            Assert.True((await CreateRepo(ctx).AddVirtualDomainOwnerAsync("EXT", 999)).IsFailure);
        }

        [Fact]
        public async Task AddVirtualDomainOwner_WhenValid_CreatesOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            var result = await CreateRepo(ctx).AddVirtualDomainOwnerAsync("EXT", user.Id);
            Assert.True(result.IsSuccess);
            Assert.Equal("EXT", result.Value.DomainId);
            Assert.Single(result.Value.Owners);
            Assert.Equal(user.Id, result.Value.Owners[0].OwnerId);
            Assert.Equal("alice@weesky.be", result.Value.Owners[0].OwnerEmail);
            Assert.Single(ctx.DomainsOwnerships);
        }

        [Fact]
        public async Task AddVirtualDomainOwner_WithSecondUser_AddsSecondOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            var bob = AddUser(ctx, "bob", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            var result = await CreateRepo(ctx).AddVirtualDomainOwnerAsync("EXT", bob.Id);
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value.Owners.Count);
            Assert.Equal(2, ctx.DomainsOwnerships.Count());
        }

        [Fact]
        public async Task AddVirtualDomainOwner_WhenSameUserAlreadyOwns_IsIdempotent()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            var result = await CreateRepo(ctx).AddVirtualDomainOwnerAsync("EXT", alice.Id);
            Assert.True(result.IsSuccess);
            Assert.Single(ctx.DomainsOwnerships);
        }

        // ── RemoveVirtualDomainOwner ───────────────────────────────────

        [Fact]
        public async Task RemoveVirtualDomainOwner_WhenNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True((await CreateRepo(ctx).RemoveVirtualDomainOwnerAsync("EXT", 1)).IsFailure);
        }

        [Fact]
        public async Task RemoveVirtualDomainOwner_WhenValid_RemovesOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", user.Id);
            var result = await CreateRepo(ctx).RemoveVirtualDomainOwnerAsync("EXT", user.Id);
            Assert.True(result.IsSuccess);
            Assert.Empty(ctx.DomainsOwnerships);
        }

        [Fact]
        public async Task RemoveVirtualDomainOwner_WhenMultipleOwners_OnlyRemovesSpecified()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            var bob = AddUser(ctx, "bob", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            AddOwnership(ctx, "EXT", bob.Id);
            var result = await CreateRepo(ctx).RemoveVirtualDomainOwnerAsync("EXT", alice.Id);
            Assert.True(result.IsSuccess);
            Assert.Single(ctx.DomainsOwnerships);
            Assert.Equal(bob.Id, ctx.DomainsOwnerships.Single().UserId);
        }
    }
}
