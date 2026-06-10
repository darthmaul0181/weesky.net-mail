using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class AdminRepositoryTests
    {
        private static TestDbContext CreateContext() => new(Guid.NewGuid().ToString());

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
        public void IsAdmin_WhenDomainNotFound_ReturnsFalse()
        {
            using var ctx = CreateContext();
            Assert.False(new AdminRepository(ctx).IsAdmin("alice", "unknown.com"));
        }

        [Fact]
        public void IsAdmin_WhenUserNotInDomain_ReturnsFalse()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            Assert.False(new AdminRepository(ctx).IsAdmin("nobody", "weesky.be"));
        }

        [Fact]
        public void IsAdmin_WhenUserAdminFlagIsN_ReturnsFalse()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY", admin: ActiveState.N);
            Assert.False(new AdminRepository(ctx).IsAdmin("alice", "weesky.be"));
        }

        [Fact]
        public void IsAdmin_WhenUserAdminFlagIsY_ReturnsTrue()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
            Assert.True(new AdminRepository(ctx).IsAdmin("alice", "weesky.be"));
        }

        [Fact]
        public void IsAdmin_IsCaseInsensitiveForUsername()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
            Assert.True(new AdminRepository(ctx).IsAdmin("ALICE", "weesky.be"));
        }

        // ── GetAllUsers ───────────────────────────────────────

        [Fact]
        public void GetAllUsers_WithNoUsers_ReturnsEmpty()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            Assert.Empty(new AdminRepository(ctx).GetAllUsers());
        }

        [Fact]
        public void GetAllUsers_ReturnsAllUsersWithDomainName()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            AddUser(ctx, "bob", "WSY");
            var users = new AdminRepository(ctx).GetAllUsers().ToList();
            Assert.Equal(2, users.Count);
            Assert.All(users, u => Assert.Equal("weesky.be", u.DomainName));
        }

        [Fact]
        public void GetAllUsers_MapsActiveFlagCorrectly()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "active", "WSY", active: ActiveState.Y);
            AddUser(ctx, "inactive", "WSY", active: ActiveState.N);
            var users = new AdminRepository(ctx).GetAllUsers().ToList();
            Assert.True(users.First(u => u.UserName == "active").Active);
            Assert.False(users.First(u => u.UserName == "inactive").Active);
        }

        [Fact]
        public void GetAllUsers_MapsAdminFlagCorrectly()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "superuser", "WSY", admin: ActiveState.Y);
            AddUser(ctx, "regular", "WSY", admin: ActiveState.N);
            var users = new AdminRepository(ctx).GetAllUsers().ToList();
            Assert.True(users.First(u => u.UserName == "superuser").Admin);
            Assert.False(users.First(u => u.UserName == "regular").Admin);
        }

        [Fact]
        public void GetAllUsers_MapsLastLoginsWhenPresent()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            var ts = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            ctx.LastLogins.Add(new LastLogin { UserId = "alice@weesky.be", Service = "imap", LastAccess = ts });
            ctx.SaveChanges();

            var users = new AdminRepository(ctx).GetAllUsers().ToList();
            var alice = users.Single(u => u.UserName == "alice");
            Assert.Single(alice.LastLogins);
            Assert.Equal("imap", alice.LastLogins[0].Service);
            Assert.Equal(new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc), alice.LastLogins[0].At);
        }

        [Fact]
        public void GetAllUsers_ReturnsEmptyLastLoginsWhenNone()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            var users = new AdminRepository(ctx).GetAllUsers().ToList();
            Assert.Empty(users.Single().LastLogins);
        }

        [Fact]
        public void GetAllUsers_OrdersLastLoginsMostRecentFirst()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            var older = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var newer = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            ctx.LastLogins.Add(new LastLogin { UserId = "alice@weesky.be", Service = "lmtp", LastAccess = older });
            ctx.LastLogins.Add(new LastLogin { UserId = "alice@weesky.be", Service = "imap", LastAccess = newer });
            ctx.SaveChanges();

            var logins = new AdminRepository(ctx).GetAllUsers().Single().LastLogins;
            Assert.Equal("imap", logins[0].Service);
            Assert.Equal("lmtp", logins[1].Service);
        }

        // ── GetUserById ───────────────────────────────────────

        [Fact]
        public void GetUserById_WhenNotFound_ReturnsNull()
        {
            using var ctx = CreateContext();
            Assert.Null(new AdminRepository(ctx).GetUserById(999));
        }

        [Fact]
        public void GetUserById_WhenFound_ReturnsUserWithDomainName()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.Y, quotaMb: 2048, fullName: "Alice Smith");

            var info = new AdminRepository(ctx).GetUserById(user.Id);

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
        public void CreateUser_WhenUsernameBlank_ReturnsFailure()
        {
            using var ctx = CreateContext();
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "   ", DomainId = "WSY", Password = "pw" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void CreateUser_WhenPasswordNull_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = null });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void CreateUser_WhenPasswordEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void CreateUser_WhenDomainNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "alice", DomainId = "ZZZ", Password = "pw" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void CreateUser_WhenUsernameAlreadyExistsInDomain_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "pw" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void CreateUser_DuplicateCheckIsCaseInsensitive()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "ALICE", DomainId = "WSY", Password = "pw" });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void CreateUser_WithValidRequest_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var result = new AdminRepository(ctx).CreateUser(new AdminUserRequest
            {
                UserName = "alice", DomainId = "WSY", Password = "secret",
                FullName = "Alice Smith", QuotaMb = 2048, Active = true, Admin = false
            });
            Assert.True(result.IsSuccess);
            Assert.Equal("alice", result.Value.UserName);
            Assert.Equal("weesky.be", result.Value.DomainName);
            Assert.Equal(2048, result.Value.QuotaMb);
            Assert.True(result.Value.Active);
            Assert.False(result.Value.Admin);
        }

        [Fact]
        public void CreateUser_NormalisesUsernameLowercase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "ALICE", DomainId = "WSY", Password = "pw" });
            Assert.Equal("alice", result.Value.UserName);
        }

        [Fact]
        public void CreateUser_StoresPasswordAsPlaintext()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "mysecret" });
            Assert.Equal("mysecret", ctx.Users.First(u => u.Name == "alice").Password);
        }

        [Fact]
        public void CreateUser_AssignsAdminFlag()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var result = new AdminRepository(ctx).CreateUser(
                new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "pw", Admin = true });
            Assert.True(result.Value.Admin);
            Assert.Equal(ActiveState.Y, ctx.Users.First(u => u.Name == "alice").Admin);
        }

        // ── UpdateUser ────────────────────────────────────────

        [Fact]
        public void UpdateUser_WhenUserNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            var result = new AdminRepository(ctx).UpdateUser(999,
                new AdminUserRequest { UserName = "x", QuotaMb = 1024 });
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void UpdateUser_UpdatesFullName()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", fullName: "Old Name");
            var result = new AdminRepository(ctx).UpdateUser(user.Id,
                new AdminUserRequest { UserName = "alice", FullName = "New Name", QuotaMb = 1024 });
            Assert.True(result.IsSuccess);
            Assert.Equal("New Name", result.Value.FullName);
        }

        [Fact]
        public void UpdateUser_UpdatesQuota()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", quotaMb: 1024);
            var result = new AdminRepository(ctx).UpdateUser(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 4096 });
            Assert.Equal(4096, result.Value.QuotaMb);
        }

        [Fact]
        public void UpdateUser_WhenPasswordNull_DoesNotChangePassword()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            var originalPw = ctx.Users.First(u => u.Id == user.Id).Password;
            new AdminRepository(ctx).UpdateUser(user.Id,
                new AdminUserRequest { UserName = "alice", Password = null, QuotaMb = 1024 });
            Assert.Equal(originalPw, ctx.Users.First(u => u.Id == user.Id).Password);
        }

        [Fact]
        public void UpdateUser_WhenPasswordEmpty_DoesNotChangePassword()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            var originalPw = ctx.Users.First(u => u.Id == user.Id).Password;
            new AdminRepository(ctx).UpdateUser(user.Id,
                new AdminUserRequest { UserName = "alice", Password = "", QuotaMb = 1024 });
            Assert.Equal(originalPw, ctx.Users.First(u => u.Id == user.Id).Password);
        }

        [Fact]
        public void UpdateUser_WhenPasswordProvided_UpdatesPasswordAsPlaintext()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            new AdminRepository(ctx).UpdateUser(user.Id,
                new AdminUserRequest { UserName = "alice", Password = "newpass", QuotaMb = 1024 });
            Assert.Equal("newpass", ctx.Users.First(u => u.Id == user.Id).Password);
        }

        [Fact]
        public void UpdateUser_UpdatesActiveFlag()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", active: ActiveState.Y);
            var result = new AdminRepository(ctx).UpdateUser(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 1024, Active = false });
            Assert.False(result.Value.Active);
        }

        [Fact]
        public void UpdateUser_UpdatesAdminFlag()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.N);
            var result = new AdminRepository(ctx).UpdateUser(user.Id,
                new AdminUserRequest { UserName = "alice", QuotaMb = 1024, Admin = true });
            Assert.True(result.Value.Admin);
        }

        // ── DeleteUser ────────────────────────────────────────

        [Fact]
        public void DeleteUser_WhenNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).DeleteUser(999).IsFailure);
        }

        [Fact]
        public void DeleteUser_WhenFound_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            Assert.True(new AdminRepository(ctx).DeleteUser(user.Id).IsSuccess);
        }

        [Fact]
        public void DeleteUser_RemovesUserFromDatabase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            var user = AddUser(ctx, "alice", "WSY");
            new AdminRepository(ctx).DeleteUser(user.Id);
            Assert.False(ctx.Users.Any(u => u.Id == user.Id));
        }

        // ── GetAllDomains ─────────────────────────────────────

        [Fact]
        public void GetAllDomains_WithNoDomains_ReturnsEmpty()
        {
            using var ctx = CreateContext();
            Assert.Empty(new AdminRepository(ctx).GetAllDomains());
        }

        [Fact]
        public void GetAllDomains_ReturnsAllDomains()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddDomain(ctx, "TST", "test.com");
            var domains = new AdminRepository(ctx).GetAllDomains().ToList();
            Assert.Equal(2, domains.Count);
            Assert.Contains(domains, d => d.Id == "WSY" && d.Name == "weesky.be");
            Assert.Contains(domains, d => d.Id == "TST" && d.Name == "test.com");
        }

        // ── CreateDomain ──────────────────────────────────────

        [Fact]
        public void CreateDomain_WhenIdEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).CreateDomain(
                new AdminDomainRequest { Id = "", Name = "test.com" }).IsFailure);
        }

        [Fact]
        public void CreateDomain_WhenIdTooLong_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).CreateDomain(
                new AdminDomainRequest { Id = "ABCD", Name = "test.com" }).IsFailure);
        }

        [Fact]
        public void CreateDomain_WhenNameEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).CreateDomain(
                new AdminDomainRequest { Id = "TST", Name = "" }).IsFailure);
        }

        [Fact]
        public void CreateDomain_WhenIdAlreadyExists_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            Assert.True(new AdminRepository(ctx).CreateDomain(
                new AdminDomainRequest { Id = "WSY", Name = "other.com" }).IsFailure);
        }

        [Fact]
        public void CreateDomain_WithValidRequest_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            var result = new AdminRepository(ctx).CreateDomain(
                new AdminDomainRequest { Id = "TST", Name = "test.com" });
            Assert.True(result.IsSuccess);
            Assert.Equal("test.com", result.Value.Name);
        }

        [Fact]
        public void CreateDomain_NormalisesIdToUppercase()
        {
            using var ctx = CreateContext();
            var result = new AdminRepository(ctx).CreateDomain(
                new AdminDomainRequest { Id = "tst", Name = "test.com" });
            Assert.Equal("TST", result.Value.Id);
        }

        [Fact]
        public void CreateDomain_PersistsDomainToDatabase()
        {
            using var ctx = CreateContext();
            new AdminRepository(ctx).CreateDomain(new AdminDomainRequest { Id = "TST", Name = "test.com" });
            Assert.True(ctx.Domains.Any(d => d.Id == "TST"));
        }

        // ── UpdateDomain ──────────────────────────────────────

        [Fact]
        public void UpdateDomain_WhenNameEmpty_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).UpdateDomain("WSY",
                new AdminDomainRequest { Name = "" }).IsFailure);
        }

        [Fact]
        public void UpdateDomain_WhenDomainNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).UpdateDomain("ZZZ",
                new AdminDomainRequest { Name = "new.com" }).IsFailure);
        }

        [Fact]
        public void UpdateDomain_WithValidRequest_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var result = new AdminRepository(ctx).UpdateDomain("WSY",
                new AdminDomainRequest { Name = "new.weesky.be" });
            Assert.True(result.IsSuccess);
            Assert.Equal("new.weesky.be", result.Value.Name);
        }

        [Fact]
        public void UpdateDomain_PersistsNameChangeToDatabase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            new AdminRepository(ctx).UpdateDomain("WSY", new AdminDomainRequest { Name = "updated.be" });
            Assert.Equal("updated.be", ctx.Domains.First(d => d.Id == "WSY").Name);
        }

        // ── DeleteDomain ──────────────────────────────────────

        [Fact]
        public void DeleteDomain_WhenNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).DeleteDomain("ZZZ").IsFailure);
        }

        [Fact]
        public void DeleteDomain_WhenDomainHasUsers_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            AddUser(ctx, "alice", "WSY");
            Assert.True(new AdminRepository(ctx).DeleteDomain("WSY").IsFailure);
        }

        [Fact]
        public void DeleteDomain_WhenDomainEmpty_ReturnsSuccess()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            Assert.True(new AdminRepository(ctx).DeleteDomain("WSY").IsSuccess);
        }

        [Fact]
        public void DeleteDomain_RemovesDomainFromDatabase()
        {
            using var ctx = CreateContext();
            AddDomain(ctx);
            new AdminRepository(ctx).DeleteDomain("WSY");
            Assert.False(ctx.Domains.Any(d => d.Id == "WSY"));
        }

        // ── GetAllVirtualDomains ──────────────────────────────────

        private static void AddOwnership(TestDbContext ctx, string domainId, int userId)
        {
            ctx.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });
            ctx.SaveChanges();
        }

        [Fact]
        public void GetAllVirtualDomains_WithNoDomains_ReturnsEmpty()
        {
            using var ctx = CreateContext();
            Assert.Empty(new AdminRepository(ctx).GetAllVirtualDomains());
        }

        [Fact]
        public void GetAllVirtualDomains_ExcludesPrimaryDomainWithNoOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            Assert.Empty(new AdminRepository(ctx).GetAllVirtualDomains());
        }

        [Fact]
        public void GetAllVirtualDomains_IncludesPrimaryDomainWhenInOwnershipsTable()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            AddOwnership(ctx, "WSY", alice.Id);
            var result = new AdminRepository(ctx).GetAllVirtualDomains().ToList();
            Assert.Single(result);
            Assert.Equal("WSY", result[0].DomainId);
            Assert.Contains(result[0].Owners, o => o.OwnerId == alice.Id);
        }

        [Fact]
        public void GetAllVirtualDomains_ReturnsAliasDomainWithNoOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "EXT", "extra.com");
            var result = new AdminRepository(ctx).GetAllVirtualDomains().ToList();
            Assert.Single(result);
            Assert.Equal("EXT", result[0].DomainId);
            Assert.Equal("extra.com", result[0].DomainName);
            Assert.Empty(result[0].Owners);
        }

        [Fact]
        public void GetAllVirtualDomains_ReturnsAliasDomainWithOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", user.Id);
            var result = new AdminRepository(ctx).GetAllVirtualDomains().ToList();
            Assert.Single(result);
            Assert.Equal("EXT", result[0].DomainId);
            Assert.Single(result[0].Owners);
            Assert.Equal(user.Id, result[0].Owners[0].OwnerId);
            Assert.Equal("alice@weesky.be", result[0].Owners[0].OwnerEmail);
        }

        [Fact]
        public void GetAllVirtualDomains_ReturnsAliasDomainWithMultipleOwners()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            var bob = AddUser(ctx, "bob", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            AddOwnership(ctx, "EXT", bob.Id);
            var result = new AdminRepository(ctx).GetAllVirtualDomains().ToList();
            Assert.Single(result);
            Assert.Equal(2, result[0].Owners.Count);
            Assert.Contains(result[0].Owners, o => o.OwnerEmail == "alice@weesky.be");
            Assert.Contains(result[0].Owners, o => o.OwnerEmail == "bob@weesky.be");
        }

        // ── AddVirtualDomainOwner ──────────────────────────────────────

        [Fact]
        public void AddVirtualDomainOwner_WhenDomainNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).AddVirtualDomainOwner("ZZZ", 1).IsFailure);
        }

        [Fact]
        public void AddVirtualDomainOwner_WhenUserNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "EXT", "extra.com");
            Assert.True(new AdminRepository(ctx).AddVirtualDomainOwner("EXT", 999).IsFailure);
        }

        [Fact]
        public void AddVirtualDomainOwner_WhenValid_CreatesOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            var result = new AdminRepository(ctx).AddVirtualDomainOwner("EXT", user.Id);
            Assert.True(result.IsSuccess);
            Assert.Equal("EXT", result.Value.DomainId);
            Assert.Single(result.Value.Owners);
            Assert.Equal(user.Id, result.Value.Owners[0].OwnerId);
            Assert.Equal("alice@weesky.be", result.Value.Owners[0].OwnerEmail);
            Assert.Single(ctx.DomainsOwnerships);
        }

        [Fact]
        public void AddVirtualDomainOwner_WithSecondUser_AddsSecondOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            var bob = AddUser(ctx, "bob", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            var result = new AdminRepository(ctx).AddVirtualDomainOwner("EXT", bob.Id);
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value.Owners.Count);
            Assert.Equal(2, ctx.DomainsOwnerships.Count());
        }

        [Fact]
        public void AddVirtualDomainOwner_WhenSameUserAlreadyOwns_IsIdempotent()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            var result = new AdminRepository(ctx).AddVirtualDomainOwner("EXT", alice.Id);
            Assert.True(result.IsSuccess);
            Assert.Single(ctx.DomainsOwnerships);
        }

        // ── RemoveVirtualDomainOwner ───────────────────────────────────

        [Fact]
        public void RemoveVirtualDomainOwner_WhenNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).RemoveVirtualDomainOwner("EXT", 1).IsFailure);
        }

        [Fact]
        public void RemoveVirtualDomainOwner_WhenValid_RemovesOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", user.Id);
            var result = new AdminRepository(ctx).RemoveVirtualDomainOwner("EXT", user.Id);
            Assert.True(result.IsSuccess);
            Assert.Empty(ctx.DomainsOwnerships);
        }

        [Fact]
        public void RemoveVirtualDomainOwner_WhenMultipleOwners_OnlyRemovesSpecified()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            var bob = AddUser(ctx, "bob", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            AddOwnership(ctx, "EXT", bob.Id);
            var result = new AdminRepository(ctx).RemoveVirtualDomainOwner("EXT", alice.Id);
            Assert.True(result.IsSuccess);
            Assert.Single(ctx.DomainsOwnerships);
            Assert.Equal(bob.Id, ctx.DomainsOwnerships.Single().UserId);
        }
    }
}
