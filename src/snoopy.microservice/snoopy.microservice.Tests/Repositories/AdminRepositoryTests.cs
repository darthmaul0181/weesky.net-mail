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

        // ── GetAllOwnerships ──────────────────────────────────

        private static void AddOwnership(TestDbContext ctx, string domainId, int userId)
        {
            ctx.DomainsOwnerships.Add(new MailDomainOwnership { DomainId = domainId, UserId = userId });
            ctx.SaveChanges();
        }

        [Fact]
        public void GetAllOwnerships_WithNoDomains_ReturnsEmpty()
        {
            using var ctx = CreateContext();
            Assert.Empty(new AdminRepository(ctx).GetAllOwnerships());
        }

        [Fact]
        public void GetAllOwnerships_ExcludesPrimaryDomainWithNoOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            AddUser(ctx, "alice", "WSY");
            Assert.Empty(new AdminRepository(ctx).GetAllOwnerships());
        }

        [Fact]
        public void GetAllOwnerships_IncludesPrimaryDomainWhenInOwnershipsTable()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            AddOwnership(ctx, "WSY", alice.Id);
            var result = new AdminRepository(ctx).GetAllOwnerships().ToList();
            Assert.Single(result);
            Assert.Equal("WSY", result[0].DomainId);
            Assert.Equal(alice.Id, result[0].OwnerId);
        }

        [Fact]
        public void GetAllOwnerships_ReturnsExtraDomainWithNoOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "EXT", "extra.com");
            var result = new AdminRepository(ctx).GetAllOwnerships().ToList();
            Assert.Single(result);
            Assert.Equal("EXT", result[0].DomainId);
            Assert.Equal("extra.com", result[0].DomainName);
            Assert.Null(result[0].OwnerId);
            Assert.Null(result[0].OwnerEmail);
        }

        [Fact]
        public void GetAllOwnerships_ReturnsExtraDomainWithOwner()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", user.Id);
            var result = new AdminRepository(ctx).GetAllOwnerships().ToList();
            Assert.Single(result);
            Assert.Equal("EXT", result[0].DomainId);
            Assert.Equal(user.Id, result[0].OwnerId);
            Assert.Equal("alice@weesky.be", result[0].OwnerEmail);
        }

        // ── SetOwnership ──────────────────────────────────────

        [Fact]
        public void SetOwnership_WhenDomainNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).SetOwnership("ZZZ", 1).IsFailure);
        }

        [Fact]
        public void SetOwnership_WhenUserNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "EXT", "extra.com");
            Assert.True(new AdminRepository(ctx).SetOwnership("EXT", 999).IsFailure);
        }

        [Fact]
        public void SetOwnership_WhenValid_CreatesOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            var result = new AdminRepository(ctx).SetOwnership("EXT", user.Id);
            Assert.True(result.IsSuccess);
            Assert.Equal("EXT", result.Value.DomainId);
            Assert.Equal(user.Id, result.Value.OwnerId);
            Assert.Equal("alice@weesky.be", result.Value.OwnerEmail);
            Assert.Single(ctx.DomainsOwnerships);
        }

        [Fact]
        public void SetOwnership_WhenAlreadyExists_UpdatesOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var alice = AddUser(ctx, "alice", "WSY");
            var bob = AddUser(ctx, "bob", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", alice.Id);
            var result = new AdminRepository(ctx).SetOwnership("EXT", bob.Id);
            Assert.True(result.IsSuccess);
            Assert.Equal(bob.Id, result.Value.OwnerId);
            Assert.Single(ctx.DomainsOwnerships);
        }

        // ── DeleteOwnership ───────────────────────────────────

        [Fact]
        public void DeleteOwnership_WhenNotFound_ReturnsFailure()
        {
            using var ctx = CreateContext();
            Assert.True(new AdminRepository(ctx).DeleteOwnership("EXT").IsFailure);
        }

        [Fact]
        public void DeleteOwnership_WhenValid_RemovesOwnership()
        {
            using var ctx = CreateContext();
            AddDomain(ctx, "WSY", "weesky.be");
            var user = AddUser(ctx, "alice", "WSY");
            AddDomain(ctx, "EXT", "extra.com");
            AddOwnership(ctx, "EXT", user.Id);
            var result = new AdminRepository(ctx).DeleteOwnership("EXT");
            Assert.True(result.IsSuccess);
            Assert.Empty(ctx.DomainsOwnerships);
        }
    }
}
