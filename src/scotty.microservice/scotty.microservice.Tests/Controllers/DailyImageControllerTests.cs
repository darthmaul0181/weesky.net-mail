using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class DailyImageControllerTests
{
    private static readonly DailyImage Today = new("v1", "Poulpe fiction", "© Gabriel Barathieu", [1, 2, 3], DateTimeOffset.MaxValue);

    private readonly Mock<IAppSettingStore> _settings = new();
    private readonly Mock<IDailyImageService> _images = new();

    private DailyImageController Create(bool enabled = true)
    {
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new AppSetting { SettingKey = AppSettings.DailyImage, SettingValue = enabled ? "true" : "false" }]);
        _images.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Today);

        return new DailyImageController(_settings.Object, _images.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Theory]
    [InlineData(nameof(DailyImageController.GetDailyImage))]
    [InlineData(nameof(DailyImageController.GetDailyImageFile))]
    public void BothRoutesAreAnonymous(string action) =>
        Assert.NotEmpty(typeof(DailyImageController).GetMethod(action)!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), false));

    [Fact]
    public async Task Info_SwitchedOff_IsNotFoundAndNeverAsksBing()
    {
        var result = await Create(enabled: false).GetDailyImage("fr", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _images.Verify(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Info_AnswersTheCreditForTheLanguagesMarket()
    {
        var controller = Create();

        var result = await controller.GetDailyImage("fr", CancellationToken.None);

        var info = Assert.IsType<DailyImageInfo>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new DailyImageInfo("v1", "Poulpe fiction", "© Gabriel Barathieu"), info);
        Assert.Equal("no-cache", controller.Response.Headers.CacheControl.ToString());
        _images.Verify(i => i.GetAsync("fr-FR", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Info_WithoutAnImage_IsNotFound()
    {
        var controller = Create();
        _images.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DailyImage?)null);

        var result = await controller.GetDailyImage("en", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task File_ServesTheJpegForGood()
    {
        var controller = Create();

        var result = await controller.GetDailyImageFile("v1", "en", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(Today.Bytes, file.FileContents);
        Assert.Equal("public, max-age=31536000, immutable", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task File_RefusesAVersionThatIsNoLongerCurrent()
    {
        var result = await Create().GetDailyImageFile("yesterday", "en", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task File_SwitchedOff_IsNotFound()
    {
        var result = await Create(enabled: false).GetDailyImageFile("v1", "en", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _images.Verify(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
