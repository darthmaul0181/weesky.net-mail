using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class PreferencesControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();

    private readonly Mock<IUserPreferenceStore> _store = new();

    private PreferencesController CreateController()
    {
        _store.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<UserPreference>());

        return new PreferencesController(_store.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", WebmailUid)
        };
    }

    // The client never carries the defaults: it would be a second place to change them, and the
    // two would drift the first time one moved.
    [Fact]
    public async Task Get_AnswersEveryKnownKeyEvenWithNoRows()
    {
        var result = await CreateController().GetPreferences(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(ok.Value);
        Assert.Equal("30", values[UserPreferences.MailPageSize]);
        Assert.Equal("true", values[UserPreferences.MailShowPreview]);
    }

    [Fact]
    public async Task Get_LetsAStoredRowWin()
    {
        var controller = CreateController();
        _store.Setup(s => s.GetAsync(WebmailUid, It.IsAny<CancellationToken>()))
              .ReturnsAsync([new UserPreference
              {
                  UserId = WebmailUid,
                  PreferenceKey = UserPreferences.MailPageSize,
                  PreferenceValue = "50"
              }]);

        var result = await controller.GetPreferences(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(ok.Value);
        Assert.Equal("50", values[UserPreferences.MailPageSize]);
    }

    [Fact]
    public async Task Set_Returns204AndStoresUnderTheWebmailUid()
    {
        var result = await CreateController().SetPreference(
            new SetPreferenceRequest { Key = UserPreferences.MailPageSize, Value = "50" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _store.Verify(s => s.SetAsync(WebmailUid, UserPreferences.MailPageSize, "50",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Put_AcceptsAll()
    {
        var result = await CreateController().SetPreference(
            new SetPreferenceRequest { Key = UserPreferences.MailPageSize, Value = "all" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _store.Verify(s => s.SetAsync(WebmailUid, UserPreferences.MailPageSize, "all",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // The table cannot check a key or a value, so this is the only thing that can: without it
    // the store would accumulate rows nobody reads, and a value the client cannot render.
    [Theory]
    [InlineData("mail.invented", "30")]
    [InlineData(UserPreferences.MailPageSize, "37")]
    [InlineData(UserPreferences.MailShowPreview, "yes")]
    [InlineData("", "30")]
    public async Task Set_Returns400AndWritesNothingForAnythingUnknown(string key, string value)
    {
        var result = await CreateController().SetPreference(
            new SetPreferenceRequest { Key = key, Value = value }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Set_Returns400ForAMissingBody()
    {
        Assert.IsType<BadRequestObjectResult>(
            await CreateController().SetPreference(null!, CancellationToken.None));
    }
}
