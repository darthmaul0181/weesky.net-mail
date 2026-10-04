using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class AppSettingsLogoTests
{
    private readonly Mock<IAppSettingStore> _store = new();
    private readonly Mock<IAppLogoStore> _logos = new();

    private AppSettingsController CreateController() => new(_store.Object, _logos.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static FormFile File(byte[] bytes, string name) =>
        new(new MemoryStream(bytes), 0, bytes.Length, name, $"{name}.png");

    private static (IFormFile, IFormFile, IFormFile) Valid() => (
        File(AppLogoTests.Png(32, 32), "logo32"),
        File(AppLogoTests.Png(192, 192), "logo192"),
        File(AppLogoTests.Png(512, 512), "logo512"));

    [Fact]
    public async Task Set_StoresTheThreeRenditionsTogether()
    {
        var (a, b, c) = Valid();

        var result = await CreateController().SetLogo(a, b, c, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _logos.Verify(l => l.ReplaceAsync(
            It.Is<IReadOnlyDictionary<int, byte[]>>(d =>
                d.Count == 3 && d[32].Length == 64 && d[192].Length == 64 && d[512].Length == 64),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_RefusesAMissingRenditionAndStoresNothing()
    {
        var (a, b, _) = Valid();

        var result = await CreateController().SetLogo(a, b, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _logos.Verify(l => l.ReplaceAsync(It.IsAny<IReadOnlyDictionary<int, byte[]>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Set_RefusesARenditionOfTheWrongSizeAndStoresNothing()
    {
        var (a, _, c) = Valid();

        var result = await CreateController().SetLogo(a, File(AppLogoTests.Png(512, 512), "logo192"), c, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _logos.Verify(l => l.ReplaceAsync(It.IsAny<IReadOnlyDictionary<int, byte[]>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Refused on the declared length, before a byte is copied into memory.
    [Fact]
    public async Task Set_RefusesAnOversizedRendition()
    {
        var (_, b, c) = Valid();
        var oversized = new Mock<IFormFile>();
        oversized.SetupGet(f => f.Length).Returns(16 * 1024 + 1);

        var result = await CreateController().SetLogo(oversized.Object, b, c, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        oversized.Verify(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        oversized.Verify(f => f.OpenReadStream(), Times.Never);
        _logos.Verify(l => l.ReplaceAsync(It.IsAny<IReadOnlyDictionary<int, byte[]>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_ClearsTheLogo()
    {
        var result = await CreateController().DeleteLogo(CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _logos.Verify(l => l.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_ServesThePngWithALongCache()
    {
        var png = AppLogoTests.Png(192, 192);
        _logos.Setup(l => l.GetImageAsync(192, It.IsAny<CancellationToken>())).ReturnsAsync(png);
        var controller = CreateController();

        var result = await controller.GetLogo(192, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(png, file.FileContents);
        Assert.Equal("public, max-age=31536000, immutable", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("Origin", controller.Response.Headers.Vary.ToString());
    }

    [Fact]
    public async Task Get_Answers404WithoutALogo()
    {
        _logos.Setup(l => l.GetImageAsync(192, It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
        var controller = CreateController();

        var result = await controller.GetLogo(192, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.False(controller.Response.Headers.ContainsKey("Cache-Control"));
        Assert.False(controller.Response.Headers.ContainsKey("Vary"));
    }

    // Never reaches the store with a size it does not hold.
    [Fact]
    public async Task Get_Answers404ForAnUnknownSize()
    {
        var result = await CreateController().GetLogo(64, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _logos.Verify(l => l.GetImageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(nameof(AppSettingsController.SetLogo))]
    [InlineData(nameof(AppSettingsController.DeleteLogo))]
    public void Writes_AreReservedToAdministrators(string action)
    {
        var authorize = Assert.Single(typeof(AppSettingsController).GetMethod(action)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminRequirement.PolicyName, authorize.Policy);
    }

    [Fact]
    public void Set_BoundsTheRequestBody()
    {
        var limit = Assert.Single(typeof(AppSettingsController).GetMethod(nameof(AppSettingsController.SetLogo))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), false).Cast<IRequestSizeLimitMetadata>());
        Assert.Equal(AppLogo.MaxRequestBytes, limit.MaxRequestBodySize);
    }

    // The login page and the manifest load it with no session.
    [Fact]
    public void Get_IsAnonymous() =>
        Assert.NotEmpty(typeof(AppSettingsController).GetMethod(nameof(AppSettingsController.GetLogo))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
}
