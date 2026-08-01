using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class AdminRepositoryTests
{
    private static TestDbContext CreateContext() => new(Guid.NewGuid().ToString());

    private static IMemoryCache CreateCache() => new MemoryCache(new MemoryCacheOptions());

    private static AdminRepository CreateRepository(TestDbContext ctx, IWebmailUserStore? webmailUsers = null,
        IMemoryCache? cache = null) =>
        new(ctx, webmailUsers ?? new Mock<IWebmailUserStore>().Object, cache ?? CreateCache(),
            NullLogger<AdminRepository>.Instance);

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
        Assert.False(await CreateRepository(ctx).IsAdminAsync("alice", "unknown.com"));
    }

    [Fact]
    public async Task IsAdmin_WhenUserNotInDomain_ReturnsFalse()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        Assert.False(await CreateRepository(ctx).IsAdminAsync("nobody", "weesky.be"));
    }

    [Fact]
    public async Task IsAdmin_WhenUserAdminFlagIsN_ReturnsFalse()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        AddUser(ctx, "alice", "WSY", admin: ActiveState.N);
        Assert.False(await CreateRepository(ctx).IsAdminAsync("alice", "weesky.be"));
    }

    [Fact]
    public async Task IsAdmin_WhenUserAdminFlagIsY_ReturnsTrue()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        Assert.True(await CreateRepository(ctx).IsAdminAsync("alice", "weesky.be"));
    }

    [Fact]
    public async Task IsAdmin_IsCaseInsensitiveForUsername()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        Assert.True(await CreateRepository(ctx).IsAdminAsync("ALICE", "weesky.be"));
    }

    [Fact]
    public async Task IsAdmin_TracksNothing()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        ctx.ChangeTracker.Clear();

        await CreateRepository(ctx).IsAdminAsync("alice", "weesky.be");

        Assert.Empty(ctx.ChangeTracker.Entries<MailUser>());
        Assert.Empty(ctx.ChangeTracker.Entries<MailDomain>());
    }

    [Fact]
    public async Task IsAdmin_ReusesTheFlagWithinTheCacheWindow()
    {
        using var ctx = CreateContext();
        using var cache = CreateCache();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        Assert.True(await CreateRepository(ctx, cache: cache).IsAdminAsync("alice", "weesky.be"));

        ctx.Users.Remove(user);
        ctx.SaveChanges();

        Assert.True(await CreateRepository(ctx, cache: cache).IsAdminAsync("alice", "weesky.be"));
    }

    // One account's flag must never answer for another, in either direction of the key.
    [Fact]
    public async Task IsAdmin_CachesPerAccount()
    {
        using var ctx = CreateContext();
        using var cache = CreateCache();
        AddDomain(ctx, "WSY", "weesky.be");
        AddDomain(ctx, "OTH", "other.com");
        AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        AddUser(ctx, "bob", "WSY", admin: ActiveState.N);
        AddUser(ctx, "alice", "OTH", admin: ActiveState.N);
        var repo = CreateRepository(ctx, cache: cache);

        Assert.True(await repo.IsAdminAsync("alice", "weesky.be"));
        Assert.False(await repo.IsAdminAsync("bob", "weesky.be"));
        Assert.False(await repo.IsAdminAsync("alice", "other.com"));
    }

    [Fact]
    public async Task UpdateUser_DropsTheCachedAdminFlag()
    {
        using var ctx = CreateContext();
        using var cache = CreateCache();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        Assert.True(await CreateRepository(ctx, cache: cache).IsAdminAsync("alice", "weesky.be"));

        await CreateRepository(ctx, cache: cache).UpdateUserAsync(user.Id,
            new AdminUserRequest { UserName = "alice", Admin = false });

        Assert.False(await CreateRepository(ctx, cache: cache).IsAdminAsync("alice", "weesky.be"));
    }

    [Fact]
    public async Task DeleteUser_DropsTheCachedAdminFlag()
    {
        using var ctx = CreateContext();
        using var cache = CreateCache();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        Assert.True(await CreateRepository(ctx, cache: cache).IsAdminAsync("alice", "weesky.be"));

        await CreateRepository(ctx, cache: cache).DeleteUserAsync(user.Id);

        Assert.False(await CreateRepository(ctx, cache: cache).IsAdminAsync("alice", "weesky.be"));
    }

    // ── GetAllUsers ───────────────────────────────────────

    [Fact]
    public async Task GetAllUsers_WithNoUsers_ReturnsEmpty()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        Assert.Empty(await CreateRepository(ctx).GetAllUsersAsync());
    }

    [Fact]
    public async Task GetAllUsers_ReturnsAllUsersWithDomainName()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        AddUser(ctx, "alice", "WSY");
        AddUser(ctx, "bob", "WSY");
        var users = (await CreateRepository(ctx).GetAllUsersAsync()).ToList();
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
        var users = (await CreateRepository(ctx).GetAllUsersAsync()).ToList();
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
        var users = (await CreateRepository(ctx).GetAllUsersAsync()).ToList();
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

        var users = (await CreateRepository(ctx).GetAllUsersAsync()).ToList();
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
        var users = (await CreateRepository(ctx).GetAllUsersAsync()).ToList();
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

        var logins = (await CreateRepository(ctx).GetAllUsersAsync()).Single().LastLogins;
        Assert.Equal("imap", logins[0].Service);
        Assert.Equal("lmtp", logins[1].Service);
    }

    // The admin list is a read: materialising the rows as tracked entities would pull every
    // password into the change tracker and keep it alive for the whole request.
    [Fact]
    public async Task GetAllUsers_TracksNothing()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        AddUser(ctx, "alice", "WSY");
        ctx.LastLogins.Add(new LastLogin { UserId = "alice@weesky.be", Service = "imap", LastAccess = 1 });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        await CreateRepository(ctx).GetAllUsersAsync();

        Assert.Empty(ctx.ChangeTracker.Entries<MailUser>());
        Assert.Empty(ctx.ChangeTracker.Entries<LastLogin>());
        Assert.Empty(ctx.ChangeTracker.Entries<MailDomain>());
    }

    // ── GetUserById ───────────────────────────────────────

    [Fact]
    public async Task GetUserById_WhenNotFound_ReturnsNull()
    {
        using var ctx = CreateContext();
        Assert.Null(await CreateRepository(ctx).GetUserByIdAsync(999));
    }

    [Fact]
    public async Task GetUserById_WhenFound_ReturnsUserWithDomainName()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.Y, quotaMb: 2048, fullName: "Alice Smith");

        var info = await CreateRepository(ctx).GetUserByIdAsync(user.Id);

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
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "   ", DomainId = "WSY", Password = "password123" });
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateUser_WhenPasswordNull_ReturnsFailure()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = null });
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateUser_WhenPasswordEmpty_ReturnsFailure()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "" });
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateUser_WhenPasswordTooShort_ReturnsFailure()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "short77" });
        Assert.True(result.IsFailure);
        Assert.Equal("Password must contain at least 8 characters", result.Error);
    }

    [Fact]
    public async Task CreateUser_WhenDomainNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "ZZZ", Password = "password123" });
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateUser_WhenUsernameAlreadyExistsInDomain_ReturnsFailure()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        AddUser(ctx, "alice", "WSY");
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "password123" });
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateUser_DuplicateCheckIsCaseInsensitive()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        AddUser(ctx, "alice", "WSY");
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "ALICE", DomainId = "WSY", Password = "password123" });
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateUser_WithValidRequest_ReturnsSuccess()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        var result = await CreateRepository(ctx).CreateUserAsync(new AdminUserRequest
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
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "ALICE", DomainId = "WSY", Password = "password123" });
        Assert.Equal("alice", result.Value.UserName);
    }

    [Fact]
    public async Task CreateUser_StoresPasswordAsPlaintext()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "mysecret" });
        Assert.Equal("mysecret", ctx.Users.First(u => u.Name == "alice").Password);
    }

    [Fact]
    public async Task CreateUser_AssignsAdminFlag()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "password123", Admin = true });
        Assert.True(result.Value.Admin);
        Assert.Equal(ActiveState.Y, ctx.Users.First(u => u.Name == "alice").Admin);
    }

    // ── UpdateUser ────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_WhenUserNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        var result = await CreateRepository(ctx).UpdateUserAsync(999,
            new AdminUserRequest { UserName = "x", QuotaMb = 1024 });
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task UpdateUser_UpdatesFullName()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", fullName: "Old Name");
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
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
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
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
        await CreateRepository(ctx).UpdateUserAsync(user.Id,
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
        await CreateRepository(ctx).UpdateUserAsync(user.Id,
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
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
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
        await CreateRepository(ctx).UpdateUserAsync(user.Id,
            new AdminUserRequest { UserName = "alice", Password = "newpass123", QuotaMb = 1024 });
        Assert.Equal("newpass123", ctx.Users.First(u => u.Id == user.Id).Password);
    }

    [Fact]
    public async Task UpdateUser_UpdatesActiveFlag()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", active: ActiveState.Y);
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
            new AdminUserRequest { UserName = "alice", QuotaMb = 1024, Active = false });
        Assert.False(result.Value.Active);
    }

    [Fact]
    public async Task UpdateUser_UpdatesAdminFlag()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.N);
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
            new AdminUserRequest { UserName = "alice", QuotaMb = 1024, Admin = true });
        Assert.True(result.Value.Admin);
    }

    // A PUT that omits a field means "leave it alone". While QuotaMb/Active/Admin were plain
    // value types they carried a non-null default, so omitting them reset the quota to 1024 and
    // revoked admin -- and the repository could not tell "false" from "not sent".
    [Fact]
    public async Task UpdateUser_WhenQuotaOmitted_KeepsTheStoredQuota()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", quotaMb: 8192);
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
            new AdminUserRequest { UserName = "alice", FullName = "Alice" });
        Assert.Equal(8192, result.Value.QuotaMb);
        Assert.Equal(8192, ctx.Users.First(u => u.Id == user.Id).QuotaMb);
    }

    [Fact]
    public async Task UpdateUser_WhenAdminOmitted_KeepsTheAdminRole()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", admin: ActiveState.Y);
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
            new AdminUserRequest { UserName = "alice", FullName = "Alice" });
        Assert.True(result.Value.Admin);
        Assert.Equal(ActiveState.Y, ctx.Users.First(u => u.Id == user.Id).Admin);
    }

    [Fact]
    public async Task UpdateUser_WhenActiveOmitted_KeepsTheAccountDeactivated()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY", active: ActiveState.N);
        var result = await CreateRepository(ctx).UpdateUserAsync(user.Id,
            new AdminUserRequest { UserName = "alice", FullName = "Alice" });
        Assert.False(result.Value.Active);
    }

    [Fact]
    public async Task CreateUser_WhenQuotaOmitted_AppliesTheDefault()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var result = await CreateRepository(ctx).CreateUserAsync(
            new AdminUserRequest { UserName = "alice", DomainId = "WSY", Password = "password123" });
        Assert.Equal(1024, result.Value.QuotaMb);
        Assert.True(result.Value.Active);
        Assert.False(result.Value.Admin);
    }

    // ── DeleteUser ────────────────────────────────────────

    [Fact]
    public async Task DeleteUser_WhenNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).DeleteUserAsync(999)).IsFailure);
    }

    [Fact]
    public async Task DeleteUser_WhenFound_ReturnsSuccess()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY");
        Assert.True((await CreateRepository(ctx).DeleteUserAsync(user.Id)).IsSuccess);
    }

    [Fact]
    public async Task DeleteUser_RemovesUserFromDatabase()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        var user = AddUser(ctx, "alice", "WSY");
        await CreateRepository(ctx).DeleteUserAsync(user.Id);
        Assert.False(ctx.Users.Any(u => u.Id == user.Id));
    }

    [Fact]
    public async Task DeleteUser_AlsoDeletesTheWebmailRow()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, id: "WSY", name: "weesky.be");
        var user = AddUser(ctx, "Alice", "WSY");
        var webmail = new Mock<IWebmailUserStore>();

        await CreateRepository(ctx, webmail.Object).DeleteUserAsync(user.Id);

        webmail.Verify(s => s.DeleteByEmailAsync("alice@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteUser_WhenWebmailDeleteThrows_StillSucceeds()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, id: "WSY", name: "weesky.be");
        var user = AddUser(ctx, "alice", "WSY");
        var webmail = new Mock<IWebmailUserStore>();
        webmail.Setup(s => s.DeleteByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await CreateRepository(ctx, webmail.Object).DeleteUserAsync(user.Id);

        Assert.True(result.IsSuccess);
        Assert.False(ctx.Users.Any(u => u.Id == user.Id));
    }

    // ── GetAllDomains ─────────────────────────────────────

    [Fact]
    public async Task GetAllDomains_WithNoDomains_ReturnsEmpty()
    {
        using var ctx = CreateContext();
        Assert.Empty(await CreateRepository(ctx).GetAllDomainsAsync());
    }

    [Fact]
    public async Task GetAllDomains_ReturnsAllDomains()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        AddDomain(ctx, "TST", "test.com");
        var domains = (await CreateRepository(ctx).GetAllDomainsAsync()).ToList();
        Assert.Equal(2, domains.Count);
        Assert.Contains(domains, d => d.Id == "WSY" && d.Name == "weesky.be");
        Assert.Contains(domains, d => d.Id == "TST" && d.Name == "test.com");
    }

    // ── CreateDomain ──────────────────────────────────────

    [Fact]
    public async Task CreateDomain_WhenIdEmpty_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).CreateDomainAsync(
            new AdminDomainRequest { Id = "", Name = "test.com" })).IsFailure);
    }

    [Fact]
    public async Task CreateDomain_WhenIdTooLong_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).CreateDomainAsync(
            new AdminDomainRequest { Id = "ABCD", Name = "test.com" })).IsFailure);
    }

    [Fact]
    public async Task CreateDomain_WhenNameEmpty_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).CreateDomainAsync(
            new AdminDomainRequest { Id = "TST", Name = "" })).IsFailure);
    }

    [Fact]
    public async Task CreateDomain_WhenIdAlreadyExists_ReturnsFailure()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        Assert.True((await CreateRepository(ctx).CreateDomainAsync(
            new AdminDomainRequest { Id = "WSY", Name = "other.com" })).IsFailure);
    }

    [Fact]
    public async Task CreateDomain_WithValidRequest_ReturnsSuccess()
    {
        using var ctx = CreateContext();
        var result = await CreateRepository(ctx).CreateDomainAsync(
            new AdminDomainRequest { Id = "TST", Name = "test.com" });
        Assert.True(result.IsSuccess);
        Assert.Equal("test.com", result.Value.Name);
    }

    [Fact]
    public async Task CreateDomain_NormalisesIdToUppercase()
    {
        using var ctx = CreateContext();
        var result = await CreateRepository(ctx).CreateDomainAsync(
            new AdminDomainRequest { Id = "tst", Name = "test.com" });
        Assert.Equal("TST", result.Value.Id);
    }

    [Fact]
    public async Task CreateDomain_PersistsDomainToDatabase()
    {
        using var ctx = CreateContext();
        await CreateRepository(ctx).CreateDomainAsync(new AdminDomainRequest { Id = "TST", Name = "test.com" });
        Assert.True(ctx.Domains.Any(d => d.Id == "TST"));
    }

    // ── UpdateDomain ──────────────────────────────────────

    [Fact]
    public async Task UpdateDomain_WhenNameEmpty_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).UpdateDomainAsync("WSY",
            new AdminDomainRequest { Name = "" })).IsFailure);
    }

    [Fact]
    public async Task UpdateDomain_WhenDomainNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).UpdateDomainAsync("ZZZ",
            new AdminDomainRequest { Name = "new.com" })).IsFailure);
    }

    [Fact]
    public async Task UpdateDomain_WithValidRequest_ReturnsSuccess()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        var result = await CreateRepository(ctx).UpdateDomainAsync("WSY",
            new AdminDomainRequest { Name = "new.weesky.be" });
        Assert.True(result.IsSuccess);
        Assert.Equal("new.weesky.be", result.Value.Name);
    }

    [Fact]
    public async Task UpdateDomain_PersistsNameChangeToDatabase()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        await CreateRepository(ctx).UpdateDomainAsync("WSY", new AdminDomainRequest { Name = "updated.be" });
        Assert.Equal("updated.be", ctx.Domains.First(d => d.Id == "WSY").Name);
    }

    // ── DeleteDomain ──────────────────────────────────────

    [Fact]
    public async Task DeleteDomain_WhenNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).DeleteDomainAsync("ZZZ")).IsFailure);
    }

    [Fact]
    public async Task DeleteDomain_WhenDomainHasUsers_ReturnsFailure()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        AddUser(ctx, "alice", "WSY");
        Assert.True((await CreateRepository(ctx).DeleteDomainAsync("WSY")).IsFailure);
    }

    [Fact]
    public async Task DeleteDomain_WhenDomainEmpty_ReturnsSuccess()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        Assert.True((await CreateRepository(ctx).DeleteDomainAsync("WSY")).IsSuccess);
    }

    [Fact]
    public async Task DeleteDomain_RemovesDomainFromDatabase()
    {
        using var ctx = CreateContext();
        AddDomain(ctx);
        await CreateRepository(ctx).DeleteDomainAsync("WSY");
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
        Assert.Empty(await CreateRepository(ctx).GetAllVirtualDomainsAsync());
    }

    [Fact]
    public async Task GetAllVirtualDomains_ExcludesPrimaryDomainWithNoOwnership()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        AddUser(ctx, "alice", "WSY");
        Assert.Empty(await CreateRepository(ctx).GetAllVirtualDomainsAsync());
    }

    [Fact]
    public async Task GetAllVirtualDomains_IncludesPrimaryDomainWhenInOwnershipsTable()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        var alice = AddUser(ctx, "alice", "WSY");
        AddOwnership(ctx, "WSY", alice.Id);
        var result = (await CreateRepository(ctx).GetAllVirtualDomainsAsync()).ToList();
        Assert.Single(result);
        Assert.Equal("WSY", result[0].DomainId);
        Assert.Contains(result[0].Owners, o => o.OwnerId == alice.Id);
    }

    [Fact]
    public async Task GetAllVirtualDomains_ReturnsAliasDomainWithNoOwner()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "EXT", "extra.com");
        var result = (await CreateRepository(ctx).GetAllVirtualDomainsAsync()).ToList();
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
        var result = (await CreateRepository(ctx).GetAllVirtualDomainsAsync()).ToList();
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
        var result = (await CreateRepository(ctx).GetAllVirtualDomainsAsync()).ToList();
        Assert.Single(result);
        Assert.Equal(2, result[0].Owners.Count);
        Assert.Contains(result[0].Owners, o => o.OwnerEmail == "alice@weesky.be");
        Assert.Contains(result[0].Owners, o => o.OwnerEmail == "bob@weesky.be");
    }

    // The four cases of "not primary, or owned" in one call: each domain must be judged on its
    // own rows, never on another domain's.
    [Fact]
    public async Task GetAllVirtualDomains_JudgesEachDomainOnItsOwnRows()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        AddDomain(ctx, "PRI", "primary-owned.be");
        AddDomain(ctx, "EXT", "extra.com");
        AddDomain(ctx, "ALS", "alias-owned.com");
        var alice = AddUser(ctx, "alice", "WSY");
        AddUser(ctx, "bob", "PRI");
        AddOwnership(ctx, "PRI", alice.Id);
        AddOwnership(ctx, "ALS", alice.Id);

        var result = (await CreateRepository(ctx).GetAllVirtualDomainsAsync()).ToList();

        Assert.Equal(["ALS", "EXT", "PRI"], result.Select(d => d.DomainId).Order());
        Assert.Empty(result.Single(d => d.DomainId == "EXT").Owners);
        Assert.Equal("alice@weesky.be", result.Single(d => d.DomainId == "PRI").Owners.Single().OwnerEmail);
    }

    // ── AddVirtualDomainOwner ──────────────────────────────────────

    [Fact]
    public async Task AddVirtualDomainOwner_WhenDomainNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).AddVirtualDomainOwnerAsync("ZZZ", 1)).IsFailure);
    }

    [Fact]
    public async Task AddVirtualDomainOwner_WhenUserNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "EXT", "extra.com");
        Assert.True((await CreateRepository(ctx).AddVirtualDomainOwnerAsync("EXT", 999)).IsFailure);
    }

    [Fact]
    public async Task AddVirtualDomainOwner_WhenValid_CreatesOwnership()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        var user = AddUser(ctx, "alice", "WSY");
        AddDomain(ctx, "EXT", "extra.com");
        var result = await CreateRepository(ctx).AddVirtualDomainOwnerAsync("EXT", user.Id);
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
        var result = await CreateRepository(ctx).AddVirtualDomainOwnerAsync("EXT", bob.Id);
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
        var result = await CreateRepository(ctx).AddVirtualDomainOwnerAsync("EXT", alice.Id);
        Assert.True(result.IsSuccess);
        Assert.Single(ctx.DomainsOwnerships);
    }

    // ── RemoveVirtualDomainOwner ───────────────────────────────────

    [Fact]
    public async Task RemoveVirtualDomainOwner_WhenNotFound_ReturnsFailure()
    {
        using var ctx = CreateContext();
        Assert.True((await CreateRepository(ctx).RemoveVirtualDomainOwnerAsync("EXT", 1)).IsFailure);
    }

    [Fact]
    public async Task RemoveVirtualDomainOwner_WhenValid_RemovesOwnership()
    {
        using var ctx = CreateContext();
        AddDomain(ctx, "WSY", "weesky.be");
        var user = AddUser(ctx, "alice", "WSY");
        AddDomain(ctx, "EXT", "extra.com");
        AddOwnership(ctx, "EXT", user.Id);
        var result = await CreateRepository(ctx).RemoveVirtualDomainOwnerAsync("EXT", user.Id);
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
        var result = await CreateRepository(ctx).RemoveVirtualDomainOwnerAsync("EXT", alice.Id);
        Assert.True(result.IsSuccess);
        Assert.Single(ctx.DomainsOwnerships);
        Assert.Equal(bob.Id, ctx.DomainsOwnerships.Single().UserId);
    }
}
