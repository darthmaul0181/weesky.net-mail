using Microsoft.EntityFrameworkCore;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Fixtures;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;
using static weesky.Scotty.Microservice.Tests.Fixtures.CalendarStoreTestFactory;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class CalendarEventStoreBirthdaysTests : IAsyncLifetime
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly string db = Guid.NewGuid().ToString();
    private readonly Guid user = Guid.NewGuid();
    private Guid contact;
    private Guid regular;
    private Calendar birthdays = null!;
    private CalendarEvent birthday = null!;

    public async Task InitializeAsync()
    {
        // Seeded through a context no projector watches: the guard arms on the store's own context (R1).
        await using (var seed = new PreferencesTestDbContext(db))
        {
            contact = Guid.NewGuid();
            seed.Contacts.Add(new Contact
            {
                Id = contact, UserId = user, Uid = contact.ToString(),
                FirstName = "Ada", LastName = "Lovelace", Birthday = "1986-06-21",
            });
            await seed.SaveChangesAsync();
        }

        var store = Calendars(db);
        regular = (await store.EnsureDefaultAsync(user, Zone, None)).Id;
        await store.EnsureBirthdaysAsync(user, Zone, "en", None);

        await using var read = new PreferencesTestDbContext(db);
        birthdays = await read.Calendars.AsNoTracking().SingleAsync(c => c.Kind == CalendarKinds.Birthdays && c.UserId == user);
        birthday = await read.CalendarEvents.AsNoTracking().SingleAsync(e => e.CalendarId == birthdays.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CalendarEvent Stored() =>
        new PreferencesTestDbContext(db).CalendarEvents.AsNoTracking().Single(e => e.Id == birthday.Id);

    [Fact]
    public async Task Create_update_delete_and_move_into_birthdays_are_refused_read_only()
    {
        var created = await Events(db).CreateAsync(user, Write(birthdays.Id), None);
        var updated = await Events(db).UpdateAsync(user, birthday.Id, EditScope.All, null, Write(birthdays.Id), null, None);
        var movedOut = await Events(db).UpdateAsync(user, birthday.Id, EditScope.All, null, Write(regular), null, None);
        var deleted = await Events(db).DeleteAsync(user, birthday.Id, EditScope.All, null, None);
        var mine = (await Events(db).CreateAsync(user, Write(regular), None)).Value.EventId;
        var movedIn = await Events(db).UpdateAsync(user, mine, EditScope.All, null, Write(birthdays.Id), null, None);

        Assert.All([created, updated, movedOut, deleted, movedIn], r => Assert.Equal(CalendarStore.ReadOnly, r.Error));
        Assert.Equal(birthday.IcsHash, Stored().IcsHash);
        Assert.Single(new PreferencesTestDbContext(db).CalendarEvents.Where(e => e.CalendarId == birthdays.Id));
    }

    [Fact]
    public async Task Import_into_birthdays_is_refused()
    {
        var outcome = await Events(db).ImportAsync(user, birthdays.Id, birthday.IcsRaw.Replace("birthday-", "copy-"), None);

        Assert.Equal(0, outcome.Created);
        Assert.Equal(CalendarStore.ReadOnly, Assert.Single(outcome.Errors).Reason);
        Assert.Single(new PreferencesTestDbContext(db).CalendarEvents.Where(e => e.CalendarId == birthdays.Id));
    }

    [Fact]
    public async Task The_window_stamps_contact_and_birth_year_on_birthdays_only()
    {
        var copy = await Events(db).ImportAsync(user, regular, birthday.IcsRaw.Replace("UID:birthday-", "UID:copy-"), None);
        Assert.Equal(1, copy.Created);

        var window = await Events(db).WindowAsync(user, Utc(2026, 6, 20), Utc(2026, 6, 23), Zone, None);

        Assert.True(window.IsSuccess);
        var ours = Assert.Single(window.Value, o => o.CalendarId == birthdays.Id);
        var theirs = Assert.Single(window.Value, o => o.CalendarId == regular);
        Assert.Equal(contact, ours.ContactId);
        Assert.Equal(1986, ours.BirthYear);
        Assert.Null(theirs.ContactId);
        Assert.Null(theirs.BirthYear);
    }

    [Fact]
    public async Task A_browser_west_of_the_calendar_sees_the_birthday_on_its_own_date()
    {
        // Los Angeles midnights, 20 to 23 June: what the grid asks for in that browser.
        var window = await Events(db).WindowAsync(user, Utc(2026, 6, 20, 7), Utc(2026, 6, 23, 7), "America/Los_Angeles", None);

        var only = Assert.Single(window.Value);
        Assert.True(only.IsAllDay);
        Assert.Equal(new DateOnly(2026, 6, 21), only.StartDate);
        Assert.Equal(new DateOnly(2026, 6, 22), only.EndDateExclusive);
    }

    [Fact]
    public async Task Search_stamps_the_contact_too()
    {
        var found = await Events(db).SearchAsync(user, "Lovelace", None);

        var only = Assert.Single(found);
        Assert.Equal(contact, only.ContactId);
        Assert.Equal(1986, only.BirthYear);
    }
}
