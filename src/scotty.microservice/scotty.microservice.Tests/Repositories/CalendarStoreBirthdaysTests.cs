using Microsoft.EntityFrameworkCore;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar;
using weesky.Scotty.Microservice.Tests.Fixtures;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class CalendarStoreBirthdaysTests
{
    private const string Zone = CalendarStoreTestFactory.Zone;
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly string db = Guid.NewGuid().ToString();
    private readonly Guid user = Guid.NewGuid();

    // Seeded through a context no projector watches: the guard arms on the store's own context (R1).
    private async Task<Guid> Contact(string first, string last, string? birthday, Guid? owner = null)
    {
        await using var seed = new PreferencesTestDbContext(db);
        var contact = new Contact
        {
            Id = Guid.NewGuid(), UserId = owner ?? user, Uid = Guid.NewGuid().ToString(),
            FirstName = first, LastName = last, Birthday = birthday,
        };
        seed.Contacts.Add(contact);
        await seed.SaveChangesAsync();
        return contact.Id;
    }

    private async Task Prefer(string key, string value, Guid? owner = null)
    {
        await using var context = new PreferencesTestDbContext(db);
        await new UserPreferenceStore(context).SetAsync(owner ?? user, key, value, None);
    }

    private PreferencesTestDbContext Read() => new(db);

    private Calendar? Birthdays(Guid? owner = null) =>
        Read().Calendars.AsNoTracking().FirstOrDefault(c => c.UserId == (owner ?? user) && c.Kind == CalendarKinds.Birthdays);

    private List<CalendarEvent> EventsOf(Guid calendarId) =>
        [.. Read().CalendarEvents.AsNoTracking().Where(e => e.CalendarId == calendarId)];

    private async Task<Calendar> EnsuredAsync(string language = BirthdayLanguages.En)
    {
        var store = CalendarStoreTestFactory.Calendars(db);
        await store.EnsureDefaultAsync(user, Zone, None);
        await store.EnsureBirthdaysAsync(user, Zone, language, None);
        return Birthdays()!;
    }

    [Fact]
    public async Task Ensure_creates_the_calendar_once_filled_with_the_birthdays()
    {
        await Contact("Ada", "Lovelace", "1986-06-21");
        await Contact("Alan", "Turing", "1912-06-23");

        await EnsuredAsync(BirthdayLanguages.Fr);
        await CalendarStoreTestFactory.Calendars(db).EnsureBirthdaysAsync(user, Zone, BirthdayLanguages.Fr, None);

        var row = Assert.Single(Read().Calendars.Where(c => c.UserId == user && c.Kind == CalendarKinds.Birthdays));
        Assert.Equal("birthdays", row.DavName);
        Assert.Equal("Anniversaires", row.DisplayName);
        Assert.Equal("#be185d", row.Color);
        Assert.Equal(BirthdayReminders.SameDay, row.BirthdayReminder);
        Assert.Equal(BirthdayLanguages.Fr, row.BirthdayLanguage);
        Assert.Equal(Zone, row.TimeZone);
        Assert.Equal(1, row.Order);
        Assert.Equal(2, EventsOf(row.Id).Count);
        Assert.NotNull(Read().CalendarSyncStates.Find(row.Id));
        var listed = await CalendarStoreTestFactory.Calendars(db).ListAsync(user, None);
        Assert.Equal((CalendarKinds.Birthdays, BirthdayReminders.SameDay), listed.Select(c => (c.Kind, c.BirthdayReminder)).Last());
        Assert.Equal((CalendarKinds.Regular, (string?)null), listed.Select(c => (c.Kind, c.BirthdayReminder)).First());
    }

    [Fact]
    public async Task Ensure_does_nothing_while_the_preference_is_off()
    {
        await Contact("Ada", "Lovelace", "1986-06-21");
        await Prefer(UserPreferences.CalendarBirthdays, "off");

        await EnsuredAsync();

        Assert.Null(Birthdays());
    }

    [Fact]
    public async Task Ensure_takes_birthdays_2_when_a_client_owns_birthdays()
    {
        var store = CalendarStoreTestFactory.Calendars(db);
        await store.EnsureDefaultAsync(user, Zone, None);
        Assert.True((await store.CreateNamedAsync(user, "birthdays", new CalendarWrite("Mine", null, null, null), None)).IsSuccess);

        await store.EnsureBirthdaysAsync(user, Zone, BirthdayLanguages.En, None);

        Assert.Equal("birthdays-2", Birthdays()!.DavName);
        Assert.Equal("Birthdays", Birthdays()!.DisplayName);
    }

    [Fact]
    public async Task Ensure_losing_the_race_answers_quietly_with_the_winners_calendar()
    {
        await CalendarStoreTestFactory.Calendars(db).EnsureDefaultAsync(user, Zone, None);
        var context = new RacingPreferencesDbContext(db, () =>
            CalendarStoreTestFactory.Calendars(db).EnsureBirthdaysAsync(user, Zone, BirthdayLanguages.Fr, None));

        await CalendarStoreTestFactory.Calendars(context, new TestCalendarSyncStore(context))
            .EnsureBirthdaysAsync(user, Zone, BirthdayLanguages.En, None);

        Assert.Equal("Anniversaires", Assert.Single(Read().Calendars.Where(c => c.UserId == user && c.Kind == CalendarKinds.Birthdays)).DisplayName);
    }

    // Inside DavCredentialStore.EnableAsync's transaction our own row stays visible after a later write
    // fails; InMemory keeps it too, which is that case: mistaking it for a race would commit it half-filled.
    [Fact]
    public async Task Ensure_rethrows_a_failure_after_its_own_row_landed()
    {
        var context = new FailingAfterFirstSaveDbContext(db);

        await Assert.ThrowsAsync<DbUpdateException>(() => CalendarStoreTestFactory.Calendars(context, new TestCalendarSyncStore(context))
            .EnsureBirthdaysAsync(user, Zone, BirthdayLanguages.En, None));
    }

    private sealed class FailingAfterFirstSaveDbContext(string databaseName) : PreferencesTestDbContext(databaseName)
    {
        private bool saved;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (saved) throw new DbUpdateException("a write after the calendar row failed");
            saved = true;
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    [Theory]
    [InlineData("en", "fr", "en")]
    [InlineData("auto", "fr", "fr")]
    [InlineData("auto", "de", "en")]
    public async Task The_language_is_ui_language_then_the_request_then_english(string ui, string request, string expected)
    {
        await Prefer(UserPreferences.UiLanguage, ui);

        await EnsuredAsync(request);

        Assert.Equal(expected, Birthdays()!.BirthdayLanguage);
    }

    [Fact]
    public async Task Disabling_removes_the_calendar_its_events_and_writes_no_revision()
    {
        await Contact("Ada", "Lovelace", "1986-06-21");
        var calendar = await EnsuredAsync();

        await CalendarStoreTestFactory.Calendars(db).SetBirthdaysEnabledAsync(user, false, Zone, BirthdayLanguages.En, None);

        await using var read = Read();
        Assert.Null(Birthdays());
        Assert.Empty(EventsOf(calendar.Id));
        Assert.Empty(read.CalendarRevisions.Where(r => r.CalendarId == calendar.Id));
        Assert.Empty(read.CalendarTombstones.Where(t => t.CalendarId == calendar.Id));
        Assert.Null(read.CalendarSyncStates.Find(calendar.Id));
        Assert.Contains(read.UserPreferences, p => p.UserId == user && p.PreferenceKey == UserPreferences.CalendarBirthdays && p.PreferenceValue == "off");
    }

    [Fact]
    public async Task Enabling_again_recreates_and_fills_it()
    {
        await Contact("Ada", "Lovelace", "1986-06-21");
        var first = await EnsuredAsync();
        await CalendarStoreTestFactory.Calendars(db).SetBirthdaysEnabledAsync(user, false, Zone, BirthdayLanguages.En, None);

        await CalendarStoreTestFactory.Calendars(db).SetBirthdaysEnabledAsync(user, true, Zone, BirthdayLanguages.En, None);

        var again = Birthdays();
        Assert.NotNull(again);
        Assert.NotEqual(first.Id, again.Id);
        Assert.Single(EventsOf(again.Id));
        Assert.Contains(Read().UserPreferences, p => p.UserId == user && p.PreferenceKey == UserPreferences.CalendarBirthdays && p.PreferenceValue == "on");
    }

    [Fact]
    public async Task Deleting_it_through_the_api_is_refused()
    {
        var calendar = await EnsuredAsync();

        var deleted = await CalendarStoreTestFactory.Calendars(db).DeleteAsync(user, calendar.Id, None);

        Assert.Equal(CalendarStore.ReadOnly, deleted.Error);
        Assert.NotNull(Birthdays());
    }

    [Fact]
    public async Task Changing_the_reminder_rewrites_every_event_under_one_rank()
    {
        await Contact("Ada", "Lovelace", "1986-06-21");
        await Contact("Alan", "Turing", "1912-06-23");
        var calendar = await EnsuredAsync();
        var (store, sync) = CalendarStoreTestFactory.CalendarsWithSync(db);

        var updated = await store.UpdateAsync(user, calendar.Id,
            new CalendarWrite("Birthdays", null, null, null, BirthdayReminder: BirthdayReminders.WeekBefore), None);

        Assert.True(updated.IsSuccess);
        Assert.Equal(1, sync.RankCalls);
        Assert.Equal(BirthdayReminders.WeekBefore, Birthdays()!.BirthdayReminder);
        var events = EventsOf(calendar.Id);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(9540,
            IcsComposer.MinutesBefore(IcsDocument.MasterOf(IcsDocument.TryLoad(e.IcsRaw)!)!.Alarms.Single())));
        Assert.Single(events.Select(e => e.SyncSequence).Distinct());
    }

    [Fact]
    public async Task A_reminder_on_a_regular_calendar_is_refused()
    {
        var store = CalendarStoreTestFactory.Calendars(db);
        var regular = await store.EnsureDefaultAsync(user, Zone, None);

        var updated = await store.UpdateAsync(user, regular.Id,
            new CalendarWrite("Personal", null, null, null, BirthdayReminder: BirthdayReminders.None), None);

        Assert.Equal(CalendarStore.NotBirthdays, updated.Error);
        Assert.Null(Read().Calendars.Find(regular.Id)!.BirthdayReminder);
    }

    [Fact]
    public async Task An_unknown_reminder_is_refused()
    {
        var calendar = await EnsuredAsync();

        var updated = await CalendarStoreTestFactory.Calendars(db).UpdateAsync(user, calendar.Id,
            new CalendarWrite("Birthdays", null, null, null, BirthdayReminder: "monthly"), None);

        Assert.Equal(CalendarStore.BadReminder, updated.Error);
        Assert.Equal(BirthdayReminders.SameDay, Birthdays()!.BirthdayReminder);
    }

    [Fact]
    public async Task Setting_the_same_language_writes_nothing_and_another_rewrites()
    {
        await Contact("Ada", "Lovelace", "1986-06-21");
        var calendar = await EnsuredAsync(BirthdayLanguages.En);

        var (same, sameSync) = CalendarStoreTestFactory.CalendarsWithSync(db);
        await same.SetBirthdayLanguageAsync(user, BirthdayLanguages.En, None);
        Assert.Equal(0, sameSync.RankCalls);

        var (other, otherSync) = CalendarStoreTestFactory.CalendarsWithSync(db);
        await other.SetBirthdayLanguageAsync(user, BirthdayLanguages.Fr, None);
        Assert.Equal(1, otherSync.RankCalls);
        Assert.Equal(BirthdayLanguages.Fr, Birthdays()!.BirthdayLanguage);
        Assert.Contains("Naissance", Assert.Single(EventsOf(calendar.Id)).IcsRaw);

        var (back, backSync) = CalendarStoreTestFactory.CalendarsWithSync(db);
        await back.SetBirthdayLanguageAsync(user, BirthdayLanguages.En, None);
        Assert.Equal(1, backSync.RankCalls);
        Assert.Contains("Born 1986", Assert.Single(EventsOf(calendar.Id)).IcsRaw);
    }
}
