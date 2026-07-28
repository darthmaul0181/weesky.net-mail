using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class AppSettingsTests
{
    [Fact]
    public void Effective_AnswersEveryKeyWithItsDefault()
    {
        var values = AppSettings.Effective([]);

        Assert.Equal("false", values[AppSettings.Installable]);
        Assert.Equal("Snoopy mail", values[AppSettings.Name]);
        Assert.Equal("Snoopy", values[AppSettings.ShortName]);
    }

    [Fact]
    public void Effective_LetsAStoredRowWin()
    {
        var values = AppSettings.Effective(
            [new AppSetting { SettingKey = AppSettings.Name, SettingValue = "Weesky Mail" }]);

        Assert.Equal("Weesky Mail", values[AppSettings.Name]);
    }

    // The registry is the only safeguard: the table can check neither the key nor the value, so
    // a row that has become invalid must give way to the default rather than reach the client.
    [Fact]
    public void Effective_IgnoresAStoredRowTheRegistryNoLongerAccepts()
    {
        var values = AppSettings.Effective(
            [new AppSetting { SettingKey = AppSettings.Installable, SettingValue = "yes" }]);

        Assert.Equal("false", values[AppSettings.Installable]);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void IsValid_AcceptsTheTwoBooleans(string value)
        => Assert.True(AppSettings.IsValid(AppSettings.Installable, value));

    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    public void IsValid_RefusesAnythingElseForABoolean(string value)
        => Assert.False(AppSettings.IsValid(AppSettings.Installable, value));

    [Fact]
    public void IsValid_RefusesAnUnknownKey()
        => Assert.False(AppSettings.IsValid("app.colour", "red"));

    [Fact]
    public void IsValid_RefusesAValueThatIsOnlyWhitespace()
        => Assert.False(AppSettings.IsValid(AppSettings.Name, "   "));

    [Fact]
    public void IsValid_MeasuresLengthAfterTrimming()
    {
        Assert.True(AppSettings.IsValid(AppSettings.ShortName, "  Snoopy  "));
        Assert.False(AppSettings.IsValid(AppSettings.ShortName, "Snoopy webmail"));
        Assert.True(AppSettings.IsValid(AppSettings.Name, new string('x', 60)));
        Assert.False(AppSettings.IsValid(AppSettings.Name, new string('x', 61)));
    }

    // What is stored is what the icon will show: a leading space typed by mistake must not end
    // up under the icon.
    [Fact]
    public void Normalize_TrimsAName()
        => Assert.Equal("Snoopy", AppSettings.Normalize(AppSettings.ShortName, "  Snoopy  "));

    [Fact]
    public void Normalize_LeavesABooleanAlone()
        => Assert.Equal("true", AppSettings.Normalize(AppSettings.Installable, "true"));
}
