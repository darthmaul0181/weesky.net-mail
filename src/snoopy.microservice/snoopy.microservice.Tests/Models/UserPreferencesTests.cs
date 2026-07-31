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
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailNotifySound);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailNotifyDesktop);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailAlwaysShowImages);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailShowSpamScore);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailReadingPane);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailComposeFormat);
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailShowFolderIcons);
    }

    [Theory]
    [InlineData(UserPreferences.MailPageSize, "all")]
    [InlineData(UserPreferences.MailShowPreview, "true")]
    [InlineData(UserPreferences.MailNotifySound, "false")]
    [InlineData(UserPreferences.MailNotifyDesktop, "false")]
    [InlineData(UserPreferences.MailAlwaysShowImages, "false")]
    [InlineData(UserPreferences.MailShowSpamScore, "true")]
    [InlineData(UserPreferences.MailReadingPane, "right")]
    [InlineData(UserPreferences.MailComposeFormat, "html")]
    [InlineData(UserPreferences.MailShowFolderIcons, "false")]
    public void Default_IsTheValueAnAccountWithNoRowsGets(string key, string expected)
    {
        Assert.Equal(expected, UserPreferences.All.Single(p => p.Key == key).Default);
    }

    [Theory]
    [InlineData(UserPreferences.MailPageSize, "10", true)]
    [InlineData(UserPreferences.MailPageSize, "100", true)]
    [InlineData(UserPreferences.MailPageSize, "all", true)]
    [InlineData(UserPreferences.MailPageSize, "ALL", false)]   // the value is a symbol, not prose
    [InlineData(UserPreferences.MailPageSize, "37", false)]      // not one of the offered steps
    [InlineData(UserPreferences.MailPageSize, "0", false)]
    [InlineData(UserPreferences.MailPageSize, "abc", false)]
    [InlineData(UserPreferences.MailShowPreview, "false", true)]
    [InlineData(UserPreferences.MailShowPreview, "yes", false)]
    [InlineData(UserPreferences.MailNotifySound, "true", true)]
    [InlineData(UserPreferences.MailNotifySound, "1", false)]
    [InlineData(UserPreferences.MailNotifyDesktop, "false", true)]
    [InlineData(UserPreferences.MailNotifyDesktop, "yes", false)]
    [InlineData(UserPreferences.MailAlwaysShowImages, "true", true)]
    [InlineData(UserPreferences.MailAlwaysShowImages, "yes", false)]
    [InlineData(UserPreferences.MailShowSpamScore, "false", true)]
    [InlineData(UserPreferences.MailShowSpamScore, "yes", false)]
    [InlineData(UserPreferences.MailReadingPane, "right", true)]
    [InlineData(UserPreferences.MailReadingPane, "bottom", true)]
    [InlineData(UserPreferences.MailReadingPane, "none", true)]
    [InlineData(UserPreferences.MailReadingPane, "left", false)]
    [InlineData(UserPreferences.MailReadingPane, "Right", false)]  // the value is a symbol, not prose
    [InlineData(UserPreferences.MailComposeFormat, "html", true)]
    [InlineData(UserPreferences.MailComposeFormat, "text", true)]
    [InlineData(UserPreferences.MailComposeFormat, "HTML", false)]
    [InlineData(UserPreferences.MailComposeFormat, "plain", false)]  // the toolbar's label is not the value
    [InlineData(UserPreferences.MailComposeFormat, "", false)]
    [InlineData(UserPreferences.MailShowFolderIcons, "true", true)]
    [InlineData(UserPreferences.MailShowFolderIcons, "on", false)]
    public void IsValid_AcceptsOnlyTheOfferedValues(string key, string value, bool expected)
    {
        Assert.Equal(expected, UserPreferences.IsValid(key, value));
    }

    public static TheoryData<string, string> Defaults()
    {
        var data = new TheoryData<string, string>();
        foreach (var definition in UserPreferences.All)
            data.Add(definition.Key, definition.Default);

        return data;
    }

    // Asked of every entry, so a key added later is covered the day it is declared. An Allowed
    // list that has drifted from its own default reads correctly — Effective answers the default
    // whatever the list says — and refuses the write that would store it, so the client cannot
    // set a preference back to the value it is already showing.
    [Theory]
    [MemberData(nameof(Defaults))]
    public void Default_IsItselfAValueTheRegistryAccepts(string key, string @default)
    {
        Assert.True(UserPreferences.IsValid(key, @default));
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

        Assert.Equal("all", effective[UserPreferences.MailPageSize]);
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

        Assert.Equal("all", effective[UserPreferences.MailPageSize]);
        Assert.DoesNotContain("mail.retired", effective.Keys);
    }

    private static UserPreference Row(string key, string value) =>
        new() { UserId = Guid.NewGuid(), PreferenceKey = key, PreferenceValue = value };

    [Fact]
    public void Effective_CapturesRecipientsByDefault()
    {
        var effective = UserPreferences.Effective([]);

        Assert.Equal("true", effective["contacts.captureRecipients"]);
    }

    // Off by default like alwaysShowImages, and for the same reason: loading a remote image tells the
    // sender the message was opened, so nothing turns that on unasked.
    [Fact]
    public void Effective_DoesNotTrustContactsByDefault()
    {
        var effective = UserPreferences.Effective([]);

        Assert.Equal("false", effective["mail.trustContacts"]);
    }

    [Fact]
    public void Effective_LeavesFolderIconsOffByDefault()
    {
        var effective = UserPreferences.Effective([]);

        Assert.Equal("false", effective[UserPreferences.MailShowFolderIcons]);
    }

    [Theory]
    [InlineData("contacts.captureRecipients")]
    [InlineData("mail.trustContacts")]
    public void IsValid_AcceptsBooleansOnly(string key)
    {
        Assert.True(UserPreferences.IsValid(key, "true"));
        Assert.True(UserPreferences.IsValid(key, "false"));
        Assert.False(UserPreferences.IsValid(key, "yes"));
    }

    // The row carried these three before the setting existed, so an account that never opens it
    // must see exactly what it saw yesterday.
    [Fact]
    public void Effective_KeepsTodaysRowIconsByDefault()
    {
        var effective = UserPreferences.Effective([]);

        Assert.Equal("seen,archive,delete", effective[UserPreferences.MailRowActions]);
    }

    [Theory]
    [InlineData("", true)]                          // every icon off is a real answer, not a blank
    [InlineData("seen", true)]
    [InlineData("seen,archive,junk,delete", true)]
    [InlineData("delete,seen", true)]                // order is the client's to render, not to store
    [InlineData("seen,seen", false)]                 // a set, so a repeat is a malformed value
    [InlineData("seen,", false)]                     // a trailing separator leaves an empty member
    [InlineData(",", false)]
    [InlineData("seen, archive", false)]             // the space makes " archive" an unknown member
    [InlineData("seen,star", false)]                 // the star is not one of the choosable icons
    [InlineData("Seen", false)]                      // the value is a symbol, not prose
    public void IsValid_ReadsRowActionsAsASubset(string value, bool expected)
    {
        Assert.Equal(expected, UserPreferences.IsValid(UserPreferences.MailRowActions, value));
    }

    // A member retired from a later build must not strand the whole row: the value stops being
    // one the registry accepts, so the account falls back to the default set.
    [Fact]
    public void Effective_FallsBackWhenAStoredIconIsNoLongerOffered()
    {
        var effective = UserPreferences.Effective([Row(UserPreferences.MailRowActions, "seen,snooze")]);

        Assert.Equal("seen,archive,delete", effective[UserPreferences.MailRowActions]);
    }
}
