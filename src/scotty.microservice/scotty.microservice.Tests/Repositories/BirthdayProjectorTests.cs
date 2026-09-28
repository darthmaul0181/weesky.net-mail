using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Contacts;
using weesky.Scotty.Microservice.Models.Dav;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar;
using weesky.Scotty.Microservice.Tests.Fixtures;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public class BirthdayProjectorTests
{
    private readonly PreferencesTestDbContext context = ContactStoreTestFactory.NewContext();
    private readonly TestCalendarSyncStore calSync;
    private readonly Mock<IContactSyncStore> contactSync;
    private readonly BirthdayProjector projector;
    private readonly ContactStore store;
    private readonly Guid userId = Guid.NewGuid();

    public BirthdayProjectorTests()
    {
        calSync = new TestCalendarSyncStore(context);
        var events = new CalendarEventStore(context, calSync, NullLogger<CalendarEventStore>.Instance);
        projector = new BirthdayProjector(context, calSync, events);
        contactSync = ContactStoreTestFactory.NewSync();
        // Saves as ContactSyncStore does in production: an archive flushes whatever is tracked.
        contactSync.Setup(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()))
            .Returns(async (ContactRevision revision, CancellationToken token) =>
            {
                context.ContactRevisions.Add(revision);
                await context.SaveChangesAsync(token);
                return true;
            });
        store = new ContactStore(context, contactSync.Object, projector);
    }

    private static ContactWrite Person(string first, string last, string? birthday) =>
        ContactStoreTestFactory.Write(first, last) with { Birthday = birthday };

    private Calendar Birthdays(Guid owner)
    {
        var calendar = new Calendar
        {
            Id = Guid.NewGuid(), UserId = owner, DavName = "birthdays", DisplayName = "Anniversaires",
            Kind = CalendarKinds.Birthdays, BirthdayReminder = BirthdayReminders.SameDay,
            BirthdayLanguage = BirthdayLanguages.Fr, TimeZone = "Europe/Brussels",
        };
        context.Calendars.Add(calendar);
        context.CalendarSyncStates.Add(new CalendarSyncState { CalendarId = calendar.Id, Epoch = Guid.NewGuid() });
        context.SaveChanges();
        return calendar;
    }

    private Task<List<CalendarEvent>> Events(Guid calendarId) =>
        context.CalendarEvents.AsNoTracking().Where(e => e.CalendarId == calendarId).ToListAsync();

    private Task<List<CalendarTombstone>> Tombstones(Guid calendarId) =>
        context.CalendarTombstones.AsNoTracking().Where(t => t.CalendarId == calendarId).ToListAsync();

    private DavContactWriter Dav() =>
        new(context, store, contactSync.Object, NullLogger<DavContactWriter>.Instance);

    private static string Card(string uid, string name, string? birthday, string? kind = null) =>
        "BEGIN:VCARD\r\nVERSION:3.0\r\n" + $"UID:{uid}\r\nFN:{name}\r\nN:{name};;;;\r\n"
        + (birthday is null ? "" : $"BDAY:{birthday}\r\n")
        + (kind is null ? "" : $"X-ADDRESSBOOKSERVER-KIND:{kind}\r\n")
        + "END:VCARD\r\n";

    [Fact]
    public async Task Creating_a_contact_with_a_birthday_adds_its_event()
    {
        var calendar = Birthdays(userId);

        var created = await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default);

        var only = Assert.Single(await Events(calendar.Id));
        Assert.Equal($"{created.Value}.ics", only.DavName);
        Assert.Equal("🎂 Ada Lovelace", only.Summary);
        Assert.Equal(1UL, only.SyncSequence);
        Assert.Equal(BirthdayIcs.UidOf(created.Value), only.Uid);
    }

    [Fact]
    public async Task Changing_the_date_rewrites_the_event_under_a_new_rank()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;

        Assert.True((await store.UpdateAsync(userId, id, Person("Ada", "Lovelace", "1815-12-10"), default)).IsSuccess);

        var only = Assert.Single(await Events(calendar.Id));
        Assert.Equal(2UL, only.SyncSequence);
        Assert.Contains("DTSTART;VALUE=DATE:18151210", only.IcsRaw);
    }

    [Fact]
    public async Task Renaming_rewrites_the_title()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;

        await store.UpdateAsync(userId, id, Person("Augusta", "King", "1986-06-21"), default);

        var only = Assert.Single(await Events(calendar.Id));
        Assert.Equal("🎂 Augusta King", only.Summary);
        Assert.Equal(2UL, only.SyncSequence);
    }

    [Fact]
    public async Task Editing_an_unrelated_field_writes_nothing_and_takes_no_rank()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;
        var before = Assert.Single(await Events(calendar.Id));
        var ranks = calSync.RankCalls;

        var updated = await store.UpdateAsync(userId, id,
            Person("Ada", "Lovelace", "1986-06-21") with { JobTitle = "Analyst" }, default);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Analyst", (await context.Contacts.AsNoTracking().SingleAsync()).JobTitle);
        Assert.Equal(ranks, calSync.RankCalls);
        var after = Assert.Single(await Events(calendar.Id));
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.SyncSequence, after.SyncSequence);
    }

    [Fact]
    public async Task Clearing_the_date_removes_the_event_and_lays_a_tombstone()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;

        // The editor clears an optional field with an empty string; null leaves the card's own.
        await store.UpdateAsync(userId, id, Person("Ada", "Lovelace", string.Empty), default);

        Assert.Empty(await Events(calendar.Id));
        var tombstone = Assert.Single(await Tombstones(calendar.Id));
        Assert.Equal($"{id}.ics", tombstone.DavName);
        Assert.Equal(2UL, tombstone.SyncSequence);
    }

    [Fact]
    public async Task Deleting_the_contact_removes_the_event_and_lays_a_tombstone()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;

        Assert.True((await store.DeleteAsync(userId, id, default)).IsSuccess);

        Assert.Empty(await Events(calendar.Id));
        Assert.Equal($"{id}.ics", Assert.Single(await Tombstones(calendar.Id)).DavName);
    }

    [Fact]
    public async Task Deleting_a_contact_a_group_names_projects_before_the_group_is_archived()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;
        var groups = new ContactGroupStore(context, store, contactSync.Object);
        var group = (await groups.CreateAsync(userId, "Family", default)).Value;
        Assert.True((await groups.AddMembersAsync(userId, group.Id, [id], default)).IsSuccess);

        Assert.True((await store.DeleteAsync(userId, id, default)).IsSuccess);

        Assert.Empty(await Events(calendar.Id));
        Assert.Equal($"{id}.ics", Assert.Single(await Tombstones(calendar.Id)).DavName);
        Assert.DoesNotContain(id.ToString(), (await context.Contacts.AsNoTracking().SingleAsync()).VCardRaw);
    }

    [Fact]
    public async Task Deleting_many_contacts_lays_a_tombstone_for_each_birthday()
    {
        var calendar = Birthdays(userId);
        var ids = new List<Guid>();
        foreach (var (first, date) in new[] { ("Ada", "1986-06-21"), ("Grace", "1906-12-09"), ("Alan", "1912-06-23") })
            ids.Add((await store.CreateAsync(userId, Person(first, "X", date), default)).Value);
        var groups = new ContactGroupStore(context, store, contactSync.Object);
        var group = (await groups.CreateAsync(userId, "Pioneers", default)).Value;
        await groups.AddMembersAsync(userId, group.Id, ids, default);
        var ranks = calSync.RankCalls;

        Assert.Equal(3, await store.DeleteManyAsync(userId, ids, includeGroups: false, default));

        Assert.Empty(await Events(calendar.Id));
        var tombstones = await Tombstones(calendar.Id);
        Assert.Equal(ids.Select(BirthdayIcs.DavNameOf).Order(), tombstones.Select(t => t.DavName).Order());
        Assert.All(tombstones, t => Assert.Equal(4UL, t.SyncSequence));
        Assert.Equal(ranks + 1, calSync.RankCalls);
    }

    [Fact]
    public async Task Importing_contacts_adds_their_birthdays_under_one_rank_per_batch()
    {
        var calendar = Birthdays(userId);
        var existing = (await store.CreateAsync(userId, Person("Ada", "Lovelace", null), default)).Value;
        var ranks = calSync.RankCalls;
        ContactImportRow Row(int line, string first, string date) => new(
            line, first, "Imported", null, false, [], null, null, Person(first, "Imported", date));
        // The merge row fills the date of a contact that already has a card, so it is archived.
        var merge = new ContactImportRow(4, "Ada", "Lovelace", null, false, [], null, null,
            Person("Ada", "Lovelace", "1815-12-10"));

        var outcome = await store.ImportAsync(userId,
            [Row(2, "Grace", "1906-12-09"), Row(3, "Alan", "1912-06-23"), merge], default);

        Assert.Equal((2, 1, 0), (outcome.Created, outcome.Merged, outcome.Failed));
        var events = await Events(calendar.Id);
        Assert.Equal(3, events.Count);
        Assert.Contains(events, e => e.DavName == BirthdayIcs.DavNameOf(existing) && e.Summary == "🎂 Ada Lovelace");
        Assert.Contains(events, e => e.Summary == "🎂 Grace Imported");
        Assert.Contains(events, e => e.Summary == "🎂 Alan Imported");
        Assert.Equal(ranks + 1, calSync.RankCalls);
        Assert.Single(await context.ContactRevisions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_backfill_projects_a_birthday_it_reconciles()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", null), default)).Value;
        // A pre-4a row: its card carries a BDAY its columns never received, and it waits in the queue.
        var row = await context.Contacts.SingleAsync();
        row.VCardRaw = Card(row.Uid, "Ada Lovelace", "1986-06-21");
        row.CardHash = string.Empty;
        await context.SaveChangesAsync();

        var outcome = await store.BackfillAsync(10, default);

        Assert.Equal(1, outcome.Processed);
        var only = Assert.Single(await Events(calendar.Id));
        Assert.Equal(BirthdayIcs.DavNameOf(id), only.DavName);
        Assert.Equal("🎂 Ada Lovelace", only.Summary);
    }

    [Fact]
    public async Task A_carddav_put_adds_then_replaces_the_event()
    {
        var calendar = Birthdays(userId);
        var dav = Dav();

        var created = await dav.PutAsync(userId, "ada.vcf", Card("ada", "Ada Lovelace", "1986-06-21"), default);
        Assert.Equal(DavWriteStatus.Created, created.Status);
        var id = (await context.Contacts.AsNoTracking().SingleAsync()).Id;
        var first = Assert.Single(await Events(calendar.Id));
        Assert.Equal(BirthdayIcs.DavNameOf(id), first.DavName);
        Assert.Equal(1UL, first.SyncSequence);

        var replaced = await dav.PutAsync(userId, "ada.vcf", Card("ada", "Ada Lovelace", "1815-12-10"), default);

        Assert.Equal(DavWriteStatus.Replaced, replaced.Status);
        var second = Assert.Single(await Events(calendar.Id));
        Assert.Contains("DTSTART;VALUE=DATE:18151210", second.IcsRaw);
        Assert.Equal(2UL, second.SyncSequence);
    }

    [Fact]
    public async Task A_carddav_delete_and_delete_all_lay_tombstones()
    {
        var calendar = Birthdays(userId);
        var dav = Dav();
        await dav.PutAsync(userId, "ada.vcf", Card("ada", "Ada Lovelace", "1986-06-21"), default);
        await dav.PutAsync(userId, "grace.vcf", Card("grace", "Grace Hopper", "1906-12-09"), default);
        await dav.PutAsync(userId, "alan.vcf", Card("alan", "Alan Turing", "1912-06-23"), default);
        var ids = await context.Contacts.AsNoTracking().ToDictionaryAsync(c => c.DavName!, c => c.Id);

        Assert.Equal(DavWriteStatus.Deleted, (await dav.DeleteAsync(userId, "ada.vcf", default)).Status);
        Assert.Equal(BirthdayIcs.DavNameOf(ids["ada.vcf"]), Assert.Single(await Tombstones(calendar.Id)).DavName);

        Assert.Equal(DavWriteStatus.Deleted, (await dav.DeleteAllAsync(userId, default)).Status);

        Assert.Empty(await Events(calendar.Id));
        Assert.Equal(ids.Values.Select(BirthdayIcs.DavNameOf).Order(),
            (await Tombstones(calendar.Id)).Select(t => t.DavName).Order());
    }

    [Fact]
    public async Task Without_a_birthdays_calendar_nothing_is_written()
    {
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;
        await store.UpdateAsync(userId, id, Person("Ada", "Lovelace", "1815-12-10"), default);
        await store.DeleteAsync(userId, id, default);

        Assert.Empty(await context.CalendarEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await context.CalendarTombstones.AsNoTracking().ToListAsync());
        Assert.Equal(0, calSync.RankCalls);
    }

    [Fact]
    public async Task A_contact_refused_by_its_own_gate_writes_no_birthday()
    {
        var calendar = Birthdays(userId);
        var heavy = Person("Ada", "Lovelace", "1986-06-21") with { Notes = new string('x', ContactStore.MaxCardBytes) };

        var created = await store.CreateAsync(userId, heavy, default);

        Assert.True(created.IsFailure);
        Assert.Empty(await Events(calendar.Id));
        Assert.Equal(0, calSync.RankCalls);
    }

    [Fact]
    public async Task A_group_never_gets_an_event()
    {
        var calendar = Birthdays(userId);

        var put = await Dav().PutAsync(userId, "family.vcf", Card("family", "Family", "1986-06-21", "group"), default);

        Assert.Equal(DavWriteStatus.Created, put.Status);
        Assert.Equal(ContactKinds.Group, (await context.Contacts.AsNoTracking().SingleAsync()).Kind);
        Assert.Empty(await Events(calendar.Id));
        Assert.Equal(0, calSync.RankCalls);
    }

    [Fact]
    public async Task Rebuild_aligns_the_calendar_on_the_contacts()
    {
        // Created before the calendar exists, so the projector has nothing to write them into.
        foreach (var (first, date) in new[] { ("Ada", "1986-06-21"), ("Grace", "1906-12-09"), ("Alan", "1912-06-23") })
            await store.CreateAsync(userId, Person(first, "X", date), default);
        await store.CreateAsync(userId, Person("Nobody", "X", null), default);
        var calendar = Birthdays(userId);
        var stale = BirthdayIcs.DavNameOf(Guid.NewGuid());
        context.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(), CalendarId = calendar.Id, UserId = userId, DavName = stale, IcsRaw = "stale",
        });
        await context.SaveChangesAsync();

        await projector.RebuildAsync(calendar, default);
        await context.SaveChangesAsync();

        var events = await Events(calendar.Id);
        Assert.Equal(3, events.Count);
        Assert.Equal(["🎂 Ada X", "🎂 Alan X", "🎂 Grace X"], events.Select(e => e.Summary).Order());
        Assert.Equal(stale, Assert.Single(await Tombstones(calendar.Id)).DavName);
        Assert.Equal(1, calSync.RankCalls);
    }

    // A contact write committing while the rebuild waits for the lock must be read, not undone.
    [Fact]
    public async Task Rebuild_reads_the_contacts_after_taking_the_lock()
    {
        var db = Guid.NewGuid().ToString();
        var own = new PreferencesTestDbContext(db);
        var inner = new TestCalendarSyncStore(own);
        var gated = new Mock<ICalendarSyncStore>();
        gated.Setup(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, CancellationToken token) =>
            {
                await using var rival = new PreferencesTestDbContext(db);
                rival.Contacts.Add(new Contact { Id = Guid.NewGuid(), UserId = userId, Uid = "late", FirstName = "Grace", Birthday = "1906-12-09" });
                await rival.SaveChangesAsync(token);
                return await inner.NextSequenceAsync(id, token);
            });
        var calendar = new Calendar
        {
            Id = Guid.NewGuid(), UserId = userId, DavName = "birthdays", Kind = CalendarKinds.Birthdays,
            BirthdayReminder = BirthdayReminders.SameDay, BirthdayLanguage = BirthdayLanguages.En, TimeZone = "Europe/Brussels",
        };
        own.Calendars.Add(calendar);
        await own.SaveChangesAsync();
        var rebuilder = new BirthdayProjector(own, gated.Object,
            new CalendarEventStore(own, gated.Object, NullLogger<CalendarEventStore>.Instance));

        await rebuilder.RebuildAsync(calendar, default);
        await own.SaveChangesAsync();

        Assert.Equal("🎂 Grace", Assert.Single(own.CalendarEvents.AsNoTracking().Where(e => e.CalendarId == calendar.Id)).Summary);
    }

    [Fact]
    public async Task A_contact_saved_while_the_calendar_is_switched_off_writes_no_event()
    {
        var db = Guid.NewGuid().ToString();
        var own = new PreferencesTestDbContext(db);
        var inner = new TestCalendarSyncStore(own);
        var gated = new Mock<ICalendarSyncStore>();
        gated.Setup(s => s.NextSequenceIfPresentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, CancellationToken token) =>
            {
                // The disable commits between the projector's read of the calendar and its rank.
                await using var rival = new PreferencesTestDbContext(db);
                rival.Calendars.Remove(await rival.Calendars.SingleAsync(c => c.Id == id, token));
                await rival.SaveChangesAsync(token);
                return await inner.NextSequenceIfPresentAsync(id, token);
            });
        var calendar = new Calendar { Id = Guid.NewGuid(), UserId = userId, DavName = "birthdays", Kind = CalendarKinds.Birthdays };
        own.Calendars.Add(calendar);
        await own.SaveChangesAsync();
        var writer = new ContactStore(own, ContactStoreTestFactory.NewSync().Object, new BirthdayProjector(own, gated.Object,
            new CalendarEventStore(own, gated.Object, NullLogger<CalendarEventStore>.Instance)));

        var created = await writer.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default);

        Assert.True(created.IsSuccess);
        Assert.True(await own.Contacts.AsNoTracking().AnyAsync(c => c.Id == created.Value));
        Assert.False(await own.CalendarEvents.AsNoTracking().AnyAsync(e => e.CalendarId == calendar.Id));
    }

    [Fact]
    public async Task Rebuild_on_an_aligned_calendar_rewrites_no_event()
    {
        var calendar = Birthdays(userId);
        await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default);
        await store.CreateAsync(userId, Person("Grace", "Hopper", "--12-09"), default);
        var ranks = calSync.RankCalls;
        var before = await Events(calendar.Id);

        await projector.RebuildAsync(calendar, default);
        await context.SaveChangesAsync();

        // The rank is taken first, as the lock that orders the rebuild against contact writes.
        Assert.Equal(ranks + 1, calSync.RankCalls);
        var after = await Events(calendar.Id);
        Assert.Equal(before.Select(e => (e.DavName, e.UpdatedAt)).Order(), after.Select(e => (e.DavName, e.UpdatedAt)).Order());
    }

    [Fact]
    public async Task The_guard_refuses_a_contact_saved_without_projection()
    {
        var context = ContactStoreTestFactory.NewContext();
        _ = new BirthdayProjector(context, new TestCalendarSyncStore(context),
            new CalendarEventStore(context, new TestCalendarSyncStore(context), NullLogger<CalendarEventStore>.Instance));
        context.Contacts.Add(new Contact { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Uid = "x", Birthday = "1986-06-21" });

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("birthday", refused.Message);
    }

    [Fact]
    public async Task The_guard_lets_a_favourite_star_through()
    {
        var calendar = Birthdays(userId);
        var id = (await store.CreateAsync(userId, Person("Ada", "Lovelace", "1986-06-21"), default)).Value;
        var ranks = calSync.RankCalls;

        Assert.True((await store.SetFavoriteAsync(userId, id, true, default)).IsSuccess);

        Assert.True((await context.Contacts.AsNoTracking().SingleAsync()).IsFavorite);
        Assert.Equal(ranks, calSync.RankCalls);
        Assert.Single(await Events(calendar.Id));
    }
}
