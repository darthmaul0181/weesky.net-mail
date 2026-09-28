using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar;
using Xunit;
using IcsEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar;

public class BirthdayIcsTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

    private static Contact Person(string? birthday, string? display = "Alice Martin") => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), UserId = Guid.NewGuid(),
        DisplayName = display, Birthday = birthday, Kind = ContactKinds.Individual,
    };

    private static IcsEvent Master(string ics) => IcsDocument.MasterOf(IcsDocument.TryLoad(ics)!)!;

    [Theory]
    [InlineData("19860621")]
    [InlineData("1986-06-21")]
    [InlineData("19860621T115900Z")]
    public void A_dated_birthday_repeats_yearly_from_the_birth_date(string raw)
    {
        var master = Master(BirthdayIcs.Compose(Person(raw), BirthdayReminders.SameDay, BirthdayLanguages.Fr, Now)!);

        Assert.Equal(new DateOnly(1986, 6, 21), DateOnly.FromDateTime(master.DtStart!.Value));
        Assert.False(master.DtStart.HasTime);
        Assert.Equal("FREQ=YEARLY", master.RecurrenceRule!.ToString());
        Assert.Equal("🎂 Alice Martin", master.Summary);
        Assert.Equal("Naissance : 1986", master.Description);
        Assert.Equal("TRANSPARENT", master.Transparency);
        Assert.Equal("PRIVATE", master.Class);
        Assert.Equal("birthday-11111111-1111-1111-1111-111111111111", master.Uid);
    }

    [Theory]
    [InlineData("--0621")]
    [InlineData("--06-21")]
    public void A_year_less_birthday_starts_in_1604_without_description(string raw)
    {
        var master = Master(BirthdayIcs.Compose(Person(raw), BirthdayReminders.None, BirthdayLanguages.En, Now)!);

        Assert.Equal(new DateOnly(1604, 6, 21), DateOnly.FromDateTime(master.DtStart!.Value));
        Assert.Null(master.Description);
        Assert.Empty(master.Alarms);
    }

    [Fact]
    public void The_twenty_ninth_of_february_falls_on_the_last_day_of_february()
    {
        var rule = Master(BirthdayIcs.Compose(Person("1988-02-29"), BirthdayReminders.None, BirthdayLanguages.En, Now)!)
            .RecurrenceRule!;

        Assert.Equal([2], rule.ByMonth);
        Assert.Equal([-1], rule.ByMonthDay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("1986-02-31")]
    [InlineData("0000-06-21")]
    [InlineData("--0230")]
    public void An_unreadable_or_impossible_date_produces_nothing(string? raw) =>
        Assert.Null(BirthdayIcs.Compose(Person(raw), BirthdayReminders.SameDay, BirthdayLanguages.Fr, Now));

    [Fact]
    public void A_group_produces_nothing()
    {
        var group = Person("1986-06-21");
        group.Kind = ContactKinds.Group;

        Assert.Null(BirthdayIcs.Compose(group, BirthdayReminders.SameDay, BirthdayLanguages.Fr, Now));
    }

    [Theory]
    [InlineData(BirthdayReminders.SameDay, -540)]
    [InlineData(BirthdayReminders.DayBefore, 900)]
    [InlineData(BirthdayReminders.WeekBefore, 9540)]
    public void The_reminder_fires_at_nine_on_the_chosen_day(string reminder, int minutesBefore)
    {
        var alarm = Master(BirthdayIcs.Compose(Person("1986-06-21"), reminder, BirthdayLanguages.Fr, Now)!).Alarms.Single();

        Assert.Equal(minutesBefore, IcsComposer.MinutesBefore(alarm));
        Assert.Equal("🎂 Alice Martin", alarm.Description);
    }

    [Fact]
    public void The_name_falls_back_from_display_name_to_names_nickname_and_organisation()
    {
        var contact = Person("1986-06-21", display: null);
        contact.FirstName = "Alice"; contact.LastName = "Martin";
        Assert.Equal("🎂 Alice Martin", Master(BirthdayIcs.Compose(contact, "none", "en", Now)!).Summary);

        contact.FirstName = null; contact.LastName = null; contact.Nickname = "Ali";
        Assert.Equal("🎂 Ali", Master(BirthdayIcs.Compose(contact, "none", "en", Now)!).Summary);

        contact.Nickname = null; contact.Organization = "ACME";
        Assert.Equal("🎂 ACME", Master(BirthdayIcs.Compose(contact, "none", "en", Now)!).Summary);

        contact.Organization = " ";
        Assert.Null(BirthdayIcs.Compose(contact, "none", "en", Now));
    }

    [Fact]
    public void A_name_with_ical_syntax_round_trips() =>
        Assert.Equal("🎂 Martin, Alice; Jr \\ 👩‍🚀",
            Master(BirthdayIcs.Compose(Person("1986-06-21", "Martin, Alice; Jr \\ 👩‍🚀"), "none", "en", Now)!).Summary);

    [Fact]
    public void The_english_description_says_born() =>
        Assert.Equal("Born 1986", Master(BirthdayIcs.Compose(Person("1986-06-21"), "none", "en", Now)!).Description);

    [Fact]
    public void Only_the_stamp_differs_between_two_compositions()
    {
        var before = IcsDocument.TryLoad(BirthdayIcs.Compose(Person("1986-06-21"), "none", "en", Now)!)!;
        var after = IcsDocument.TryLoad(BirthdayIcs.Compose(Person("1986-06-21"), "none", "en", Now.AddDays(3))!)!;

        Assert.True(IcsComposer.SameContent(before, after));
    }

    [Fact]
    public void Identify_reads_the_contact_and_the_birth_year()
    {
        var dated = IcsDocument.TryLoad(BirthdayIcs.Compose(Person("1986-06-21"), "none", "en", Now)!)!;
        var yearLess = IcsDocument.TryLoad(BirthdayIcs.Compose(Person("--0621"), "none", "en", Now)!)!;

        Assert.Equal((Person(null).Id, (int?)1986), BirthdayIcs.Identify(dated));
        Assert.Equal((Person(null).Id, (int?)null), BirthdayIcs.Identify(yearLess));
    }
}
