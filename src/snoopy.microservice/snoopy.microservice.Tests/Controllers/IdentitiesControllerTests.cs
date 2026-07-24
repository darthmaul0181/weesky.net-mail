using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class IdentitiesControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();

    private readonly Mock<ISendingIdentityStore> _store = new();
    private readonly Mock<IAliasesRepository> _aliases = new();
    private readonly Mock<IUsersRepository> _users = new();

    private IdentitiesController CreateController(
        IReadOnlyList<SendingIdentity>? stored = null, IEnumerable<Alias>? aliases = null, string? fullName = "Mick Dubois")
    {
        _store.Setup(s => s.GetAsync(WebmailUid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored ?? []);
        _aliases.Setup(a => a.GetAliasesAsync(It.IsAny<User>()))
            .ReturnsAsync(aliases ?? []);
        _users.Setup(u => u.FindByEmailAsync("mick@weesky.be"))
            .ReturnsAsync(new User("mick@weesky.be") { FullName = fullName! });

        return new IdentitiesController(_store.Object, _aliases.Object, _users.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("mick", "weesky.be", WebmailUid),
        };
    }

    private static SendingIdentity Row(string address, string name, bool isDefault = false) =>
        new() { UserId = WebmailUid, Address = address, DisplayName = name, IsDefault = isDefault };

    [Fact]
    public async Task List_MergesStoredRowsWithThePrimaryAndFlagsStale()
    {
        var controller = CreateController(
            stored: [Row("michel@weesky.be", "Michel"), Row("gone@weesky.be", "Ancien")],
            aliases: [new Alias { Name = "michel", Domain = "weesky.be" }]);

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
        var controller = CreateController(aliases: [new Alias { Name = "michel", Domain = "weesky.be" }]);
        IReadOnlyList<SendingIdentity>? written = null;
        _store.Setup(s => s.ReplaceAsync(WebmailUid, It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<SendingIdentity>, CancellationToken>((_, rows, _) => written = rows)
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
        _store.Verify(s => s.ReplaceAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<SendingIdentity>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_AStoredStaleAddressSurvivesValidation()
    {
        var controller = CreateController(stored: [Row("gone@weesky.be", "Ancien")]);
        _store.Setup(s => s.ReplaceAsync(WebmailUid, It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
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
        _store.Setup(s => s.ReplaceAsync(WebmailUid, It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await controller.Replace(new ReplaceIdentitiesRequest { Identities = null! }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.ReplaceAsync(WebmailUid,
            It.Is<IReadOnlyList<SendingIdentity>>(rows => rows.Count == 0), It.IsAny<CancellationToken>()));
    }
}
