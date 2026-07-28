using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class AppSettingsControllerTests
{
    private readonly Mock<IAppSettingStore> _store = new();

    private AppSettingsController CreateController()
    {
        _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<AppSetting>());

        return new AppSettingsController(_store.Object);
    }

    [Fact]
    public async Task Get_AnswersEveryKnownKeyEvenWithNoRows()
    {
        var result = await CreateController().GetAppSettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(ok.Value);
        Assert.Equal("false", values[AppSettings.Installable]);
        Assert.Equal("Snoopy mail", values[AppSettings.Name]);
        Assert.Equal("Snoopy", values[AppSettings.ShortName]);
    }

    [Fact]
    public async Task Get_LetsAStoredRowWin()
    {
        var controller = CreateController();
        _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([new AppSetting
              {
                  SettingKey = AppSettings.Name, SettingValue = "Weesky Mail"
              }]);

        var result = await controller.GetAppSettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(ok.Value);
        Assert.Equal("Weesky Mail", values[AppSettings.Name]);
    }

    // The install icon must live on /login, where there is no session.
    [Fact]
    public void Get_IsAnonymous()
    {
        var method = typeof(AppSettingsController).GetMethod(nameof(AppSettingsController.GetAppSettings))!;

        Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
    }

    [Fact]
    public void Set_IsReservedToAdministrators()
    {
        var method = typeof(AppSettingsController).GetMethod(nameof(AppSettingsController.SetAppSetting))!;

        var authorize = Assert.Single(
            method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminRequirement.PolicyName, authorize.Policy);
    }

    [Fact]
    public async Task Set_Returns204AndStoresTheValue()
    {
        var result = await CreateController().SetAppSetting(
            new SetAppSettingRequest { Key = AppSettings.Installable, Value = "true" },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _store.Verify(s => s.SetAsync(AppSettings.Installable, "true", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // What is stored is what the icon will display.
    [Fact]
    public async Task Set_StoresANameTrimmed()
    {
        await CreateController().SetAppSetting(
            new SetAppSettingRequest { Key = AppSettings.ShortName, Value = "  Snoopy  " },
            CancellationToken.None);

        _store.Verify(s => s.SetAsync(AppSettings.ShortName, "Snoopy", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("app.colour", "red")]
    [InlineData(AppSettings.Installable, "yes")]
    [InlineData(AppSettings.ShortName, "   ")]
    [InlineData(AppSettings.ShortName, "Snoopy webmail")]
    public async Task Set_Returns400OnAnythingTheRegistryRefuses(string key, string value)
    {
        var result = await CreateController().SetAppSetting(
            new SetAppSettingRequest { Key = key, Value = value }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Set_Returns400OnAnEmptyBody()
    {
        var result = await CreateController().SetAppSetting(null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
