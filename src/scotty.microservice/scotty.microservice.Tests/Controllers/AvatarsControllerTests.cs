using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class AvatarsControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly AvatarImage Picture = new([1, 2, 3], "image/png");

    private readonly Mock<IUserPreferenceStore> _preferences = new();
    private readonly Mock<IAvatarService> _avatars = new();

    private AvatarsController Create(string showAvatars = "true")
    {
        _preferences.Setup(s => s.GetAsync(WebmailUid, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new UserPreference { UserId = WebmailUid, PreferenceKey = UserPreferences.MailShowAvatars, PreferenceValue = showAvatars }]);
        _avatars.Setup(a => a.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Picture);

        return new AvatarsController(_preferences.Object, _avatars.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", WebmailUid)
        };
    }

    [Fact]
    public void TheRouteIsOneAuthenticatedGet_UnderTheAvatarsPolicy()
    {
        var action = Assert.Single(ControllerRouteSurface.Of([typeof(AvatarsController)]));

        Assert.Equal("GET api/Avatars", $"{action.Verb} {action.Route}");
        Assert.NotNull(action.Controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(action.Method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("avatars", action.Controller.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("TRUE")]
    public async Task Get_WhenAvatarsAreOff_Answers404_WithoutCallingTheService(string stored)
    {
        var result = await Create(stored).GetAvatar(new AvatarRequest("bob@example.com"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _avatars.Verify(a => a.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_WhenNoPreferenceIsStored_Answers404_WithoutCallingTheService()
    {
        var controller = Create();
        _preferences.Setup(s => s.GetAsync(WebmailUid, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await controller.GetAvatar(new AvatarRequest("bob@example.com"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _avatars.Verify(a => a.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<string?> InvalidAddresses =>
    [
        null,
        "",
        "nope",
        "a@b@c",
        new string('a', 243) + "@example.com",
        "Bob <bob@x.com>",
    ];

    [Theory]
    [MemberData(nameof(InvalidAddresses))]
    public async Task Get_WithAnInvalidAddress_Answers400(string? address)
    {
        var result = await Create().GetAvatar(new AvatarRequest(address), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _avatars.Verify(a => a.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_WhenTheServiceHasNoImage_Answers404()
    {
        var controller = Create();
        _avatars.Setup(a => a.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((AvatarImage?)null);

        var result = await controller.GetAvatar(new AvatarRequest("bob@example.com"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Get_OnAHit_Answers200WithBytes_ContentType_NoSniff_AndPrivateCache()
    {
        var controller = Create();

        var result = await controller.GetAvatar(new AvatarRequest("bob@example.com"), CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(Picture.Bytes, file.FileContents);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("private, max-age=86400", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Get_ForwardsTheAddressAsGiven_NotTheCallersIdentity()
    {
        await Create().GetAvatar(new AvatarRequest("Bob@Example.com"), CancellationToken.None);

        _avatars.Verify(a => a.GetAsync("Bob@Example.com", It.IsAny<CancellationToken>()), Times.Once);
        _avatars.VerifyNoOtherCalls();
    }
}
