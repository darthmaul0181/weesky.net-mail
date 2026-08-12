using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class IdentitiesControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();

    private readonly Mock<ISendingIdentityStore> _store = new();
    private readonly Mock<IAliasDirectory> _directory = new();
    private readonly Mock<IProfileReader> _profiles = new();
    private readonly Mock<IConnectedAccountStore> _accounts = new();

    private IdentitiesController CreateController(
        IReadOnlyList<SendingIdentity>? stored = null, IReadOnlyList<string>? aliases = null,
        string? fullName = "Mick Dubois", string? accountIdHeader = null, string? accountIdQuery = null,
        bool enforcesOwnership = true)
    {
        _store.Setup(s => s.GetAsync(WebmailUid, AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored ?? []);
        _directory.SetupGet(d => d.EnforcesOwnership).Returns(enforcesOwnership);
        _directory.Setup(d => d.GetAddressesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aliases ?? []);
        _profiles.Setup(p => p.GetDisplayNameAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullName);

        var controller = new IdentitiesController(_store.Object, _directory.Object, _profiles.Object, _accounts.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("mick", "weesky.be", WebmailUid),
        };
        if (accountIdHeader is not null)
            controller.ControllerContext.HttpContext.Request.Headers[IAccountConnectionResolver.HeaderName] = accountIdHeader;
        if (accountIdQuery is not null)
            controller.ControllerContext.HttpContext.Request.QueryString =
                QueryString.Create(IAccountConnectionResolver.QueryName, accountIdQuery);
        return controller;
    }

    private static SendingIdentity Row(string address, string name, bool isDefault = false) =>
        new() { UserId = WebmailUid, Address = address, DisplayName = name, IsDefault = isDefault };

    [Fact]
    public async Task List_MergesStoredRowsWithThePrimaryAndFlagsStale()
    {
        var controller = CreateController(
            stored: [Row("michel@weesky.be", "Michel"), Row("gone@weesky.be", "Ancien")],
            aliases: ["michel@weesky.be"]);

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IdentityListResponse>(ok.Value);
        Assert.Equal(3, response.Identities.Count);
        Assert.True(Assert.Single(response.Identities, i => i.IsPrimary).IsDefault);
        Assert.True(Assert.Single(response.Identities, i => i.Address == "gone@weesky.be").Stale);
    }

    [Fact]
    public async Task Replace_ValidSet_Returns204AndWritesCanonicalRows()
    {
        var controller = CreateController(aliases: ["michel@weesky.be"]);
        IReadOnlyList<SendingIdentity>? written = null;
        _store.Setup(s => s.ReplaceAsync(WebmailUid, AccountScope.Primary, It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IReadOnlyList<SendingIdentity>, CancellationToken>(
                (_, _, rows, _) => written = rows)
            .Returns(Task.CompletedTask);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "Michel@Weesky.BE", DisplayName = "Michel", IsDefault = true }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("michel@weesky.be", Assert.Single(written!).Address);
    }

    [Fact]
    public async Task Replace_ForeignAddress_Returns400NamingIt()
    {
        var controller = CreateController();

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "intruder@evil.com", DisplayName = "X" }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("intruder@evil.com", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _store.Verify(s => s.ReplaceAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<IReadOnlyList<SendingIdentity>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_AStoredStaleAddressSurvivesValidation()
    {
        var controller = CreateController(stored: [Row("gone@weesky.be", "Ancien")]);
        _store.Setup(s => s.ReplaceAsync(WebmailUid, AccountScope.Primary, It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "gone@weesky.be", DisplayName = "Ancien" }],
        };

        Assert.IsType<NoContentResult>(await controller.Replace(request, CancellationToken.None));
    }

    [Fact]
    public async Task Replace_NullListClearsEverything()
    {
        var controller = CreateController();
        _store.Setup(s => s.ReplaceAsync(WebmailUid, AccountScope.Primary, It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await controller.Replace(new ReplaceIdentitiesRequest { Identities = null! }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.ReplaceAsync(WebmailUid, AccountScope.Primary,
            It.Is<IReadOnlyList<SendingIdentity>>(rows => rows.Count == 0), It.IsAny<CancellationToken>()));
    }

    // ── Connected-account scope ──────────────────────────────────────────────

    private static ConnectedAccount Account(Guid id) =>
        new() { Id = id, UserId = WebmailUid, Email = "me@remote.com" };

    [Fact]
    public async Task List_ExplicitPrimaryHeader_UsesThePrimaryPath()
    {
        var controller = CreateController(
            stored: [Row("michel@weesky.be", "Michel")],
            aliases: ["michel@weesky.be"],
            accountIdHeader: "primary");

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IdentityListResponse>(ok.Value);
        Assert.Equal(2, response.Identities.Count);
        _accounts.Verify(a => a.FindAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_ConnectedAccountId_ResolvesFromTheStoreScopedToThatAccount()
    {
        var id = Guid.NewGuid();
        var controller = CreateController(accountIdHeader: id.ToString());
        _accounts.Setup(a => a.FindAsync(WebmailUid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Account(id));
        _store.Setup(s => s.GetAsync(WebmailUid, id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { UserId = WebmailUid, AccountId = id.ToString(), Address = "me@remote.com", DisplayName = "Me", IsDefault = true }]);

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IdentityListResponse>(ok.Value);
        var identity = Assert.Single(response.Identities);
        Assert.Equal("me@remote.com", identity.Address);
        Assert.True(identity.IsPrimary);
        Assert.True(identity.IsDefault);
        _store.Verify(s => s.GetAsync(WebmailUid, id.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_UnknownOrForeignAccountId_Returns404()
    {
        var id = Guid.NewGuid();
        var controller = CreateController(accountIdHeader: id.ToString());
        _accounts.Setup(a => a.FindAsync(WebmailUid, id, It.IsAny<CancellationToken>())).ReturnsAsync((ConnectedAccount?)null);

        var result = await controller.List(CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, Assert.IsType<ResultEnveloppe>(notFound.Value).Message);
    }

    [Fact]
    public async Task List_UnparsableAccountId_Returns404()
    {
        var controller = CreateController(accountIdHeader: "not-a-guid");

        var result = await controller.List(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _accounts.Verify(a => a.FindAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_ConnectedAccountId_ValidatesThenReplacesScopedToThatAccount()
    {
        var id = Guid.NewGuid();
        var controller = CreateController(accountIdHeader: id.ToString());
        _accounts.Setup(a => a.FindAsync(WebmailUid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Account(id));
        IReadOnlyList<SendingIdentity>? written = null;
        _store.Setup(s => s.ReplaceAsync(WebmailUid, id.ToString(), It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IReadOnlyList<SendingIdentity>, CancellationToken>((_, _, rows, _) => written = rows)
            .Returns(Task.CompletedTask);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "ME@Remote.com", DisplayName = "Me", IsDefault = true }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var row = Assert.Single(written!);
        Assert.Equal("me@remote.com", row.Address);
        Assert.True(row.IsDefault);
    }

    [Fact]
    public async Task Replace_ConnectedAccount_MissingAccountAddress_Returns400()
    {
        var id = Guid.NewGuid();
        var controller = CreateController(accountIdHeader: id.ToString());
        _accounts.Setup(a => a.FindAsync(WebmailUid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Account(id));

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "other@remote.com", DisplayName = "Other" }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.ReplaceAsync(It.IsAny<Guid>(), id.ToString(), It.IsAny<IReadOnlyList<SendingIdentity>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // The transport is header *or* ?account=, decoded by IAccountConnectionResolver.AccountIdFrom.
    // Reading the header alone made both verbs answer — and write — the primary's identities.

    [Fact]
    public async Task List_AccountIdInTheQueryString_ResolvesTheConnectedAccount()
    {
        var id = Guid.NewGuid();
        var controller = CreateController(
            stored: [Row("michel@weesky.be", "Michel")], accountIdQuery: id.ToString());
        _accounts.Setup(a => a.FindAsync(WebmailUid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Account(id));
        _store.Setup(s => s.GetAsync(WebmailUid, id.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IdentityListResponse>(ok.Value);
        Assert.Equal("me@remote.com", Assert.Single(response.Identities).Address);
        _store.Verify(s => s.GetAsync(WebmailUid, AccountScope.Primary, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_AccountIdInTheQueryString_NeverWritesThePrimaryScope()
    {
        var id = Guid.NewGuid();
        var controller = CreateController(accountIdQuery: id.ToString());
        _accounts.Setup(a => a.FindAsync(WebmailUid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Account(id));
        _store.Setup(s => s.ReplaceAsync(WebmailUid, id.ToString(), It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "me@remote.com", DisplayName = "Me", IsDefault = true }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.ReplaceAsync(WebmailUid, id.ToString(), It.IsAny<IReadOnlyList<SendingIdentity>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.ReplaceAsync(It.IsAny<Guid>(), AccountScope.Primary,
            It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_UnknownOrForeignAccountId_Returns404()
    {
        var id = Guid.NewGuid();
        var controller = CreateController(accountIdHeader: id.ToString());
        _accounts.Setup(a => a.FindAsync(WebmailUid, id, It.IsAny<CancellationToken>())).ReturnsAsync((ConnectedAccount?)null);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "me@remote.com", DisplayName = "Me" }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, Assert.IsType<ResultEnveloppe>(notFound.Value).Message);
    }

    // ── Free identities: a platform that cannot vouch for an address ─────────
    // The primary mailbox is then read and written exactly as a connected account is: no alias
    // list exists to judge ownership against, so no row can be stale and any address is allowed.

    [Fact]
    public async Task List_FreeIdentities_ResolvesLikeAConnectedAccount()
    {
        var controller = CreateController(
            stored: [Row("elsewhere@other.test", "Elsewhere")], enforcesOwnership: false);

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IdentityListResponse>(ok.Value);
        Assert.Equal(2, response.Identities.Count);
        Assert.All(response.Identities, i => Assert.False(i.Stale));
        var primary = Assert.Single(response.Identities, i => i.IsPrimary);
        Assert.Equal("mick@weesky.be", primary.Address);
        Assert.True(primary.IsDefault);
        _directory.Verify(d => d.GetAddressesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _profiles.Verify(p => p.GetDisplayNameAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_FreeIdentities_AcceptsAnyWellFormedAddressUnderThePrimaryScope()
    {
        var controller = CreateController(enforcesOwnership: false);
        IReadOnlyList<SendingIdentity>? written = null;
        _store.Setup(s => s.ReplaceAsync(WebmailUid, AccountScope.Primary, It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IReadOnlyList<SendingIdentity>, CancellationToken>((_, _, rows, _) => written = rows)
            .Returns(Task.CompletedTask);

        var request = new ReplaceIdentitiesRequest
        {
            Identities =
            [
                new IdentityEntry { Address = "elsewhere@other.test", DisplayName = "Elsewhere", IsDefault = true },
                new IdentityEntry { Address = "Mick@Weesky.BE", DisplayName = "Mick" },
            ],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(2, written!.Count);
        // The primary carries the default whatever the request said — ValidateConnected's rule.
        Assert.True(Assert.Single(written, r => r.Address == "mick@weesky.be").IsDefault);
        Assert.False(Assert.Single(written, r => r.Address == "elsewhere@other.test").IsDefault);
    }

    [Fact]
    public async Task Replace_FreeIdentities_WithoutThePrimary_Returns400()
    {
        var controller = CreateController(enforcesOwnership: false);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "elsewhere@other.test", DisplayName = "Elsewhere" }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.ReplaceAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
