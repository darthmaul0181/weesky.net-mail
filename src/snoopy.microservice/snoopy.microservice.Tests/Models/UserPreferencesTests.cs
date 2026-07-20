using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class UserPreferencesTests
{
    [Fact]
    public void All_CarriesTheKeysTheClientOffers()
    {
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailPageSize);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailShowPreview);
    }

    [Theory]
    [InlineData(UserPreferences.MailPageSize, "30")]
    [InlineData(UserPreferences.MailShowPreview, "true")]
    public void Default_IsTheValueAnAccountWithNoRowsGets(string key, string expected)
    {
        Assert.Equal(expected, UserPreferences.All.Single(p => p.Key == key).Default);
    }

    [Theory]
    [InlineData(UserPreferences.MailPageSize, "10", true)]
    [InlineData(UserPreferences.MailPageSize, "100", true)]
    [InlineData(UserPreferences.MailPageSize, "37", false)]      // not one of the offered steps
    [InlineData(UserPreferences.MailPageSize, "0", false)]
    [InlineData(UserPreferences.MailPageSize, "abc", false)]
    [InlineData(UserPreferences.MailShowPreview, "false", true)]
    [InlineData(UserPreferences.MailShowPreview, "yes", false)]
    public void IsValid_AcceptsOnlyTheOfferedValues(string key, string value, bool expected)
    {
        Assert.Equal(expected, UserPreferences.IsValid(key, value));
    }

    // A key the client invented must not reach the database: the table is a key/value store,
    // so nothing else would stop it accumulating rows nobody reads.
    [Fact]
    public void IsValid_RejectsAnUnknownKey()
    {
        Assert.False(UserPreferences.IsValid("mail.somethingElse", "30"));
    }

    [Fact]
    public void Effective_FillsInEveryDefault()
    {
        var effective = UserPreferences.Effective([]);

        Assert.Equal("30", effective[UserPreferences.MailPageSize]);
        Assert.Equal("true", effective[UserPreferences.MailShowPreview]);
        Assert.Equal(UserPreferences.All.Count, effective.Count);
    }

    [Fact]
    public void Effective_LetsAStoredRowWin()
    {
        var effective = UserPreferences.Effective([Row(UserPreferences.MailPageSize, "50")]);

        Assert.Equal("50", effective[UserPreferences.MailPageSize]);
        Assert.Equal("true", effective[UserPreferences.MailShowPreview]);
    }

    // Rows outlive the code that wrote them: a preference dropped from a later version, or a
    // value narrowed since, must fall back rather than hand the client something it cannot use.
    [Theory]
    [InlineData("mail.retired", "whatever")]
    [InlineData(UserPreferences.MailPageSize, "9999")]
    public void Effective_IgnoresARowTheRegistryNoLongerAccepts(string key, string value)
    {
        var effective = UserPreferences.Effective([Row(key, value)]);

        Assert.Equal("30", effective[UserPreferences.MailPageSize]);
        Assert.DoesNotContain("mail.retired", effective.Keys);
    }

    private static UserPreference Row(string key, string value) =>
        new() { AccountId = "alice@weesky.be", PreferenceKey = key, PreferenceValue = value };
}
