# Birthdays calendar — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A read-only « Anniversaires » calendar, projected from the contacts' birthdays, stored as real events so the webmail and every CalDAV client see it, with a reminder the user chooses.

**Architecture:** The calendar is a `calendars` row of `kind = 'birthdays'`; its events are ordinary `calendar_events` rows written by one `BirthdayProjector`. Every contact write path calls `ProjectTrackedAsync` just before its `SaveChanges`, inside the transaction it already holds; a guard in `PreferencesDbContext.SaveChangesAsync` refuses a relevant contact change the projector did not see. A full `RebuildAsync` aligns the calendar on creation, on a reminder change and on a language change. The API and CalDAV refuse every write into the collection except its name, colour and order.

**Tech Stack:** ASP.NET Core 10 / EF Core (MariaDB, InMemory in tests) / Ical.Net 5 / xUnit + Moq; React 18 + TypeScript, TanStack Query v5, Vitest + Testing Library.

**Spec:** `docs/history/specs/2026-09-28-webmail-calendar-birthdays-design.md` — mockups: https://claude.ai/artifact/JgjmGBma857e5YZ4hj3pq3

## Global Constraints

- Calendar kinds: `regular` | `birthdays`. Reminders: `none` | `same_day` | `day_before` | `week_before`, default `same_day`. Languages: `fr` | `en`.
- DAV name `birthdays`, then `birthdays-2`, `birthdays-3`… if taken. Default colour `#be185d`. Display name `Anniversaires` (fr) / `Birthdays` (en).
- Event: `UID:birthday-<contactId>`, DAV name `<contactId>.ics`, `DTSTART;VALUE=DATE`, one-day `DTEND`, `RRULE:FREQ=YEARLY` (`;BYMONTH=2;BYMONTHDAY=-1` for 29 February), year-less start year **1604**, `SUMMARY:🎂 <name>`, `DESCRIPTION` `Naissance : <year>` (U+00A0 before the colon) / `Born <year>` (absent when year-less), `TRANSP:TRANSPARENT`, `CLASS:PRIVATE`, `X-SCOTTY-CONTACT-ID:<contactId>`.
- Alarm triggers relative to the all-day start: `same_day` +540 min, `day_before` −900 min, `week_before` −9540 min; `none` → no `VALARM`.
- Preference key `calendar.birthdays`, values `on` | `off`, default `on`; set only through `PUT /api/Calendars/Birthdays`.
- No `calendar_revisions` row is ever written for the birthdays calendar.
- UI strings in English and French (`’` U+2019; U+00A0 before `: ; ? !`, written ` ` in JSON). Colours are role tokens (`.claude/rules/frontend-theming.md`), except the calendar colour carried as `--cal`.
- Code comments in English, 3 lines max, only where the code is not obvious. No duplicated logic: reuse `IcsComposer`, `IcsDocument`, `CalendarEventStore.ApplyIcsAsync`, `ICalendarSyncStore`, `MenuSelect`, `ToggleRow`, `Modal`, `DeleteConfirmModal`.
- Commands: backend `dotnet test src/scotty.microservice.sln` (never `--no-build`); frontend from `src/frontend`: `npx vitest run <path>`, `npm run typecheck`, `npm run lint`. Revert `src/scotty.microservice/ApiDocumentation.xml` drift before any commit (`git checkout -- src/scotty.microservice/ApiDocumentation.xml` when the diff is unrelated).
- `Edit`/`Write` write LF on this CRLF repo: multi-line replacements in `.cs` files that already hold CRLF go through a short Python script, or `git diff` shows the whole file changed.
- Commits: two lines max, never starting or ending with `@`, via `git commit -F -` and a heredoc. Do not push.

## Review Focus

1. A contact name holding `,` `;` `\` or an emoji: the SUMMARY must round-trip exactly (Ical.Net escapes; a hand-built line would not). Pinned in Task 1.
2. A browser west of the calendar's zone (America/Los_Angeles, calendar in Europe/Brussels) must see the birthday on its own date, not the day before. Pinned in Task 3 (window test).
3. A `BDAY` of `0000-06-21` or `1986-02-31` must produce no event and no exception. Pinned in Task 1.
4. A contact deleted through the bulk delete (`DeleteManyAsync`) or through CardDAV « tout supprimer » must leave a tombstone per birthday, so phones drop it. Pinned in Task 2.
5. `ui.language` set back to `auto` must not rewrite the calendar; set to `en` then `fr` must rewrite it twice, and only when the language actually changes. Pinned in Task 3.

---

### Task 1: Schema, entity and the birthday file

**Files:**
- Modify: `install/install.sql` (the `calendars` DDL and its keys)
- Create: `docs/operations/birthdays-calendar-migration.md`
- Modify: `src/scotty.microservice/Data/Preferences/Calendar.cs`
- Modify: `src/scotty.microservice/Data/Preferences/PreferencesDbContext.cs:155-157`
- Create: `src/scotty.microservice/Models/Calendar/CalendarKinds.cs`
- Create: `src/scotty.microservice/Services/Calendar/BirthdayIcs.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Services/Calendar/BirthdayIcsTests.cs`

**Interfaces:**
- Produces: `CalendarKinds.Regular|Birthdays`, `BirthdayReminders.None|SameDay|DayBefore|WeekBefore|All|Default`, `BirthdayLanguages.Fr|En|Of(string?)`; `Calendar.Kind`, `Calendar.BirthdayReminder`, `Calendar.BirthdayLanguage`, `Calendar.BirthdaysOwner`; `BirthdayIcs.Compose(Contact, string reminder, string language, DateTime nowUtc) : string?`, `BirthdayIcs.Identify(IcsCalendar) : (Guid ContactId, int? BirthYear)?`, `BirthdayIcs.DavNameOf(Guid)`, `BirthdayIcs.UidOf(Guid)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ical.Net.CalendarComponents;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services.Calendar;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar;

public class BirthdayIcsTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

    private static Contact Person(string? birthday, string? display = "Alice Martin") => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), UserId = Guid.NewGuid(),
        DisplayName = display, Birthday = birthday, Kind = ContactKinds.Individual,
    };

    private static CalendarEvent Master(string ics) => IcsDocument.MasterOf(IcsDocument.TryLoad(ics)!)!;

    [Theory]
    [InlineData("19860621")]
    [InlineData("1986-06-21")]
    [InlineData("19860621T115900Z")]
    public void A_dated_birthday_repeats_yearly_from_the_birth_date(string raw)
    {
        var master = Master(BirthdayIcs.Compose(Person(raw), BirthdayReminders.SameDay, BirthdayLanguages.Fr, Now)!);

        Assert.Equal(new DateOnly(1986, 6, 21), DateOnly.FromDateTime(master.DtStart!.Value));
        Assert.False(master.DtStart.HasTime);
        Assert.Equal("FREQ=YEARLY", master.RecurrenceRules.Single().ToString());
        Assert.Equal("🎂 Alice Martin", master.Summary);
        Assert.Equal("Naissance : 1986", master.Description);
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
            .RecurrenceRules.Single();

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
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice.sln --filter "FullyQualifiedName~BirthdayIcsTests"`
Expected: build FAIL — `BirthdayIcs`, `BirthdayReminders`, `BirthdayLanguages` do not exist.

- [ ] **Step 3: The kinds and their vocabularies**

`src/scotty.microservice/Models/Calendar/CalendarKinds.cs`:

```csharp
namespace weesky.Scotty.Microservice.Models.Calendar;

public static class CalendarKinds
{
    public const string Regular = "regular";
    public const string Birthdays = "birthdays";
}

public static class BirthdayReminders
{
    public const string None = "none";
    public const string SameDay = "same_day";
    public const string DayBefore = "day_before";
    public const string WeekBefore = "week_before";
    public const string Default = SameDay;
    public static readonly IReadOnlyList<string> All = [None, SameDay, DayBefore, WeekBefore];
}

public static class BirthdayLanguages
{
    public const string Fr = "fr";
    public const string En = "en";

    /// <summary>The first of the candidates that is a language the file is written in; English otherwise.</summary>
    public static string Of(params string?[] candidates) =>
        candidates.FirstOrDefault(c => c is Fr or En) ?? En;
}
```

- [ ] **Step 4: The entity columns and their model**

Append to `Calendar.cs` (before `created_at`):

```csharp
    /// <summary><see cref="CalendarKinds"/>: a birthdays calendar is written by the projector alone.</summary>
    [Column("kind")]
    public string Kind { get; set; } = CalendarKinds.Regular;

    [Column("birthday_reminder")]
    public string? BirthdayReminder { get; set; }

    [Column("birthday_language")]
    public string? BirthdayLanguage { get; set; }

    /// <summary>Generated by the schema: the user id on the birthdays row, NULL elsewhere, unique.</summary>
    [Column("birthdays_owner")]
    public Guid? BirthdaysOwner { get; set; }
```

In `PreferencesDbContext.OnModelCreating`, after the `Calendar` key lines:

```csharp
        modelBuilder.Entity<Calendar>().Property(c => c.BirthdaysOwner)
            .HasComputedColumnSql("IF(kind = 'birthdays', user_id, NULL)", stored: true);
        modelBuilder.Entity<Calendar>().HasIndex(c => c.BirthdaysOwner).IsUnique();
```

- [ ] **Step 5: The schema, and the script an operator replays**

In `install/install.sql`, add to `CREATE TABLE calendars` after `user_id`:

```sql
  `kind` enum('regular','birthdays') NOT NULL DEFAULT 'regular' COMMENT 'birthdays = projected from the contacts, read-only',
```

and before `created_at`:

```sql
  `birthday_reminder` enum('none','same_day','day_before','week_before') DEFAULT NULL COMMENT 'birthdays only',
  `birthday_language` char(2) DEFAULT NULL COMMENT 'fr or en; birthdays only',
  `birthdays_owner` char(36) GENERATED ALWAYS AS (if(`kind` = 'birthdays',`user_id`,NULL)) STORED COMMENT 'At most one birthdays calendar per user',
```

and to its `ALTER TABLE calendars` keys: `ADD UNIQUE KEY ux_calendars_birthdays_owner (birthdays_owner)`.

`docs/operations/birthdays-calendar-migration.md` holds the `ALTER TABLE` of the spec's « Base de données » section verbatim, one line saying it is replayed on each database (prod and dev) before deploying, and the check query `SHOW CREATE TABLE calendars;`.

- [ ] **Step 6: The composer**

`src/scotty.microservice/Services/Calendar/BirthdayIcs.cs`:

```csharp
using System.Text.RegularExpressions;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using IcsCalendar = Ical.Net.Calendar;
using IcsEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace weesky.Scotty.Microservice.Services.Calendar;

/// <summary>The one file a contact's birthday becomes (spec, décision 5), and what the webmail reads back.</summary>
internal static partial class BirthdayIcs
{
    internal const string ContactIdProperty = "X-SCOTTY-CONTACT-ID";

    /// <summary>Apple's year for a birthday without one; a leap year, so 29 February exists.</summary>
    internal const int YearLessYear = 1604;

    [GeneratedRegex(@"^(\d{4})-?(\d{2})-?(\d{2})")]
    private static partial Regex Dated();

    [GeneratedRegex(@"^--(\d{2})-?(\d{2})$")]
    private static partial Regex YearLess();

    internal static string DavNameOf(Guid contactId) => $"{contactId}.ics";

    internal static string UidOf(Guid contactId) => $"birthday-{contactId}";

    internal static string? Compose(Contact contact, string reminder, string language, DateTime nowUtc)
    {
        if (contact.Kind != ContactKinds.Individual) return null;
        if (BirthOf(contact.Birthday) is not { } birth || NameOf(contact) is not { } name) return null;

        var summary = $"🎂 {name}";
        var evt = new IcsEvent
        {
            Uid = UidOf(contact.Id),
            DtStart = new CalDateTime(birth.Start),
            DtEnd = new CalDateTime(birth.Start.AddDays(1)),
            Summary = summary,
            Transparency = "TRANSPARENT",
            Class = "PRIVATE",
            DtStamp = IcsComposer.Utc(nowUtc),
        };
        evt.RecurrenceRules.Add(birth.Start is { Month: 2, Day: 29 }
            ? new RecurrencePattern(FrequencyType.Yearly) { ByMonth = [2], ByMonthDay = [-1] }
            : new RecurrencePattern(FrequencyType.Yearly));
        if (birth.Year is { } year)
            evt.Description = language == BirthdayLanguages.Fr ? $"Naissance : {year}" : $"Born {year}";
        evt.AddProperty(ContactIdProperty, contact.Id.ToString());
        if (MinutesAfterStart(reminder) is { } minutes)
            evt.Alarms.Add(new Alarm
            {
                Action = AlarmAction.Display, Description = summary,
                Trigger = new Trigger(Duration.FromMinutes(minutes)),
            });

        var calendar = IcsComposer.Envelope();
        calendar.Events.Add(evt);
        return IcsDocument.Serialize(calendar);
    }

    /// <summary>The contact and the birth year a file of ours carries; null on anything else.</summary>
    internal static (Guid ContactId, int? BirthYear)? Identify(IcsCalendar parsed)
    {
        var master = IcsDocument.MasterOf(parsed);
        if (master?.Properties.Get<string>(ContactIdProperty) is not { } raw || !Guid.TryParse(raw, out var id))
            return null;
        var year = master.DtStart?.Year;
        return (id, year is null or YearLessYear ? null : year);
    }

    private static int? MinutesAfterStart(string reminder) => reminder switch
    {
        BirthdayReminders.SameDay => 9 * 60,
        BirthdayReminders.DayBefore => -15 * 60,
        BirthdayReminders.WeekBefore => -(6 * 24 + 15) * 60,
        _ => null,
    };

    private static string? NameOf(Contact c) =>
        new[] { c.DisplayName, $"{c.FirstName} {c.LastName}", c.Nickname, c.Organization }
            .Select(n => n?.Trim())
            .FirstOrDefault(n => !string.IsNullOrEmpty(n));

    private readonly record struct Birth(DateOnly Start, int? Year);

    private static Birth? BirthOf(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (YearLess().Match(value) is { Success: true } yearLess)
            return Day(YearLessYear, yearLess.Groups[1].Value, yearLess.Groups[2].Value) is { } d ? new Birth(d, null) : null;
        if (Dated().Match(value) is { Success: true } dated)
            return Day(int.Parse(dated.Groups[1].Value), dated.Groups[2].Value, dated.Groups[3].Value) is { } d ? new Birth(d, d.Year) : null;
        return null;
    }

    private static DateOnly? Day(int year, string month, string day) =>
        year >= 1 && DateOnly.TryParseExact($"{year:D4}-{month}-{day}", "yyyy-MM-dd", out var d) ? d : null;
}
```

If `new CalDateTime(DateOnly)` or `Duration.FromMinutes` differ in this Ical.Net version, mirror what `IcsComposer.PlaceDates` / `PlaceReminders` already do — do not add a second way.

- [ ] **Step 7: Run to verify they pass**

Run: `dotnet test src/scotty.microservice.sln --filter "FullyQualifiedName~BirthdayIcsTests"`
Expected: PASS (all).

- [ ] **Step 8: Commit**

```bash
git add install/install.sql docs/operations/birthdays-calendar-migration.md src/scotty.microservice/Data src/scotty.microservice/Models/Calendar/CalendarKinds.cs src/scotty.microservice/Services/Calendar/BirthdayIcs.cs src/scotty.microservice/scotty.microservice.Tests/Services/Calendar/BirthdayIcsTests.cs
git commit -F - <<'EOF'
Add the birthdays calendar kind and the file a birthday becomes

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 2: The projector, every contact write path, and the guard

**Files:**
- Create: `src/scotty.microservice/Repositories/IBirthdayProjector.cs`, `src/scotty.microservice/Repositories/BirthdayProjector.cs`
- Modify: `src/scotty.microservice/Data/Preferences/PreferencesDbContext.cs` (guard)
- Modify: `src/scotty.microservice/Repositories/ContactStore.cs` (ctor, `ProjectBirthdaysAsync`, each write path, `BackfillAsync` transaction)
- Modify: `src/scotty.microservice/Repositories/DavContactWriter.cs` (each write path)
- Modify: `src/scotty.microservice/Configuration/ApplicationServicesConfiguration.cs:166`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Repositories/BirthdayProjectorTests.cs`, `.../Architecture/ContactBulkOperationTests.cs`

**Interfaces:**
- Consumes: Task 1's `BirthdayIcs`, `CalendarKinds`, `Calendar.*`.
- Produces: `IBirthdayProjector.ProjectTrackedAsync(CancellationToken)`, `IBirthdayProjector.RebuildAsync(Calendar calendar, CancellationToken)`; `BirthdayProjector.IsRelevant(EntityEntry<Contact>)`; `ContactStore(PreferencesDbContext, IContactSyncStore, IBirthdayProjector? birthdays = null)`; `ContactStore.ProjectBirthdaysAsync(CancellationToken)`; `PreferencesDbContext.BirthdayProjectionRequired`, `PreferencesDbContext.BirthdayProjected`.

- [ ] **Step 1: Write the failing tests**

`BirthdayProjectorTests.cs` builds, per test: `var context = ContactStoreTestFactory.NewContext(); var calSync = new TestCalendarSyncStore(context); var events = new CalendarEventStore(context, calSync, NullLogger<CalendarEventStore>.Instance); var projector = new BirthdayProjector(context, calSync, events); var store = new ContactStore(context, ContactStoreTestFactory.NewSync().Object, projector);` and a helper `Calendar Birthdays(Guid userId)` that adds a birthdays calendar row (`Kind = Birthdays`, `BirthdayReminder = same_day`, `BirthdayLanguage = fr`, `TimeZone = "Europe/Brussels"`) plus its `CalendarSyncState`. Helper `Task<List<CalendarEvent>> Events(Guid calendarId)`; helper `Task<List<CalendarTombstone>> Tombstones(Guid calendarId)`.

```csharp
    [Fact] public async Task Creating_a_contact_with_a_birthday_adds_its_event()
    { /* CreateAsync with Birthday "1986-06-21" → one event, DavName "<id>.ics", Summary "🎂 Ada Lovelace", SyncSequence 1 */ }

    [Fact] public async Task Changing_the_date_rewrites_the_event_under_a_new_rank() { }
    [Fact] public async Task Renaming_rewrites_the_title() { }
    [Fact] public async Task Editing_an_unrelated_field_writes_nothing_and_takes_no_rank()
    { /* Update the job title → calSync.RankCalls unchanged, event row UpdatedAt unchanged */ }
    [Fact] public async Task Clearing_the_date_removes_the_event_and_lays_a_tombstone() { }
    [Fact] public async Task Deleting_the_contact_removes_the_event_and_lays_a_tombstone() { }
    [Fact] public async Task Deleting_many_contacts_lays_a_tombstone_for_each_birthday() { }
    [Fact] public async Task Importing_contacts_adds_their_birthdays_under_one_rank_per_batch() { }
    [Fact] public async Task The_backfill_projects_a_birthday_it_reconciles() { }
    [Fact] public async Task A_carddav_put_adds_then_replaces_the_event() { }
    [Fact] public async Task A_carddav_delete_and_delete_all_lay_tombstones() { }
    [Fact] public async Task Without_a_birthdays_calendar_nothing_is_written() { }
    [Fact] public async Task A_contact_refused_by_its_own_gate_writes_no_birthday()
    { /* CreateAsync with a card over MaxCardBytes → failure, no event, RankCalls 0 */ }
    [Fact] public async Task A_group_never_gets_an_event() { }

    [Fact]
    public async Task Rebuild_aligns_the_calendar_on_the_contacts()
    { /* 3 contacts with birthdays, 1 without, 1 stale event of a deleted contact →
         after RebuildAsync: 3 events, 1 tombstone, exactly one RankCalls */ }

    [Fact]
    public async Task Rebuild_on_an_aligned_calendar_takes_no_rank() { }

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
    { /* SetFavoriteAsync on an existing contact with a birthday → no exception, no rank */ }
```

Fill each body with the arrange/act/assert its name and comment state; every assertion reads rows from `context`, never mocks.

`ContactBulkOperationTests.cs`:

```csharp
public class ContactBulkOperationTests
{
    [Fact]
    public void No_bulk_operation_touches_contacts_behind_the_projector()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Tests") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f),
                @"Contacts\s*(\.\s*\w+\s*\([^;]*\))*\s*\.\s*Execute(Delete|Update)"))
            .ToList();

        Assert.Empty(offenders);
    }
}
```

(Resolve `root` to `src/scotty.microservice/`; adjust the `../` depth to the test output folder and assert the folder holds `Program.cs` so a wrong depth fails loudly instead of scanning nothing.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice.sln --filter "FullyQualifiedName~BirthdayProjectorTests|FullyQualifiedName~ContactBulkOperationTests"`
Expected: build FAIL — `BirthdayProjector` does not exist.

- [ ] **Step 3: The projector**

`IBirthdayProjector.cs`:

```csharp
using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

/// <summary>Keeps the birthdays calendar on the contacts (spec, décision 4). Both methods run inside
/// the caller's transaction and before its SaveChanges.</summary>
public interface IBirthdayProjector
{
    /// <summary>The events of every relevant contact change the context is tracking.</summary>
    Task ProjectTrackedAsync(CancellationToken cancellationToken);

    /// <summary>The whole calendar aligned on its owner's contacts.</summary>
    Task RebuildAsync(Calendar calendar, CancellationToken cancellationToken);
}
```

`BirthdayProjector.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Services.Calendar;

namespace weesky.Scotty.Microservice.Repositories;

/// <inheritdoc cref="IBirthdayProjector"/>
internal sealed class BirthdayProjector : IBirthdayProjector
{
    private static readonly string[] Relevant =
    [
        nameof(Contact.Birthday), nameof(Contact.DisplayName), nameof(Contact.FirstName),
        nameof(Contact.LastName), nameof(Contact.Nickname), nameof(Contact.Organization),
    ];

    private readonly PreferencesDbContext context;
    private readonly ICalendarSyncStore sync;
    private readonly CalendarEventStore events;

    public BirthdayProjector(PreferencesDbContext context, ICalendarSyncStore sync, CalendarEventStore events)
    {
        this.context = context;
        this.sync = sync;
        this.events = events;
        context.BirthdayProjectionRequired = true;
    }

    internal static bool IsRelevant(EntityEntry<Contact> entry) => entry.State switch
    {
        EntityState.Added or EntityState.Deleted => entry.Entity.Kind == ContactKinds.Individual,
        EntityState.Modified => entry.Property(nameof(Contact.Kind)).IsModified
            || (entry.Entity.Kind == ContactKinds.Individual && Relevant.Any(p => entry.Property(p).IsModified)),
        _ => false,
    };

    public async Task ProjectTrackedAsync(CancellationToken cancellationToken)
    {
        // Read before any await: a rank or a tombstone flushes the tracker and detaches the deleted.
        var changed = context.ChangeTracker.Entries<Contact>().Where(IsRelevant)
            .Select(e => (e.Entity, Deleted: e.State == EntityState.Deleted)).ToList();
        foreach (var (contact, _) in changed) context.BirthdayProjected.Add(contact);

        foreach (var byUser in changed.GroupBy(c => c.Entity.UserId))
        {
            if (await BirthdaysOf(byUser.Key, cancellationToken) is not { } calendar) continue;

            var wanted = byUser.ToDictionary(c => BirthdayIcs.DavNameOf(c.Entity.Id),
                c => c.Deleted ? null : Compose(c.Entity, calendar));
            var names = wanted.Keys.ToList();
            var held = await context.CalendarEvents
                .Where(e => e.CalendarId == calendar.Id && names.Contains(e.DavName))
                .ToDictionaryAsync(e => e.DavName, cancellationToken);
            await ApplyAsync(calendar, wanted, held, cancellationToken);
        }
    }

    public async Task RebuildAsync(Calendar calendar, CancellationToken cancellationToken)
    {
        var contacts = await context.Contacts.AsNoTracking()
            .Where(c => c.UserId == calendar.UserId)
            .Select(c => new Contact
            {
                Id = c.Id, UserId = c.UserId, Kind = c.Kind, Birthday = c.Birthday, DisplayName = c.DisplayName,
                FirstName = c.FirstName, LastName = c.LastName, Nickname = c.Nickname, Organization = c.Organization,
            })
            .ToListAsync(cancellationToken);
        var held = await context.CalendarEvents.Where(e => e.CalendarId == calendar.Id)
            .ToDictionaryAsync(e => e.DavName, cancellationToken);

        var wanted = held.Keys.ToDictionary(name => name, _ => (string?)null);
        foreach (var contact in contacts)
            wanted[BirthdayIcs.DavNameOf(contact.Id)] = Compose(contact, calendar);

        await ApplyAsync(calendar, wanted, held, cancellationToken);
    }

    private Task<Calendar?> BirthdaysOf(Guid userId, CancellationToken cancellationToken) =>
        context.Calendars.FirstOrDefaultAsync(
            c => c.UserId == userId && c.Kind == CalendarKinds.Birthdays, cancellationToken);

    private static string? Compose(Contact contact, Calendar calendar) =>
        BirthdayIcs.Compose(contact, calendar.BirthdayReminder ?? BirthdayReminders.Default,
            calendar.BirthdayLanguage ?? BirthdayLanguages.En, DateTime.UtcNow);

    /// <summary>One rank for the whole change, and none when nothing differs from what is stored.</summary>
    private async Task ApplyAsync(Calendar calendar, Dictionary<string, string?> wanted,
        Dictionary<string, CalendarEvent> held, CancellationToken cancellationToken)
    {
        var changes = wanted.Where(w => !Same(held.GetValueOrDefault(w.Key), w.Value)).ToList();
        if (changes.Count == 0) return;

        var rank = await sync.NextSequenceAsync(calendar.Id, cancellationToken);
        foreach (var (davName, ics) in changes)
        {
            held.TryGetValue(davName, out var row);
            if (ics is null)
            {
                context.CalendarEvents.Remove(row!);
                await sync.PlaceTombstoneAsync(calendar.Id, davName, rank, cancellationToken);
                continue;
            }

            if (row is null)
            {
                row = new CalendarEvent { Id = Guid.NewGuid(), CalendarId = calendar.Id, UserId = calendar.UserId, DavName = davName };
                context.CalendarEvents.Add(row);
                await sync.LiftTombstoneAsync(calendar.Id, davName, cancellationToken);
            }
            await events.ApplyIcsAsync(row, calendar, ics, IcsDocument.TryLoad(ics)!, rank, cancellationToken);
        }
    }

    private static bool Same(CalendarEvent? row, string? ics) => (row, ics) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        _ => IcsDocument.TryLoad(row.IcsRaw) is { } stored && IcsComposer.SameContent(stored, IcsDocument.TryLoad(ics)!),
    };
}
```

- [ ] **Step 4: The guard**

In `PreferencesDbContext`:

```csharp
    /// <summary>Set by <c>BirthdayProjector</c>: a relevant contact change must then pass through it.</summary>
    internal bool BirthdayProjectionRequired { get; set; }

    internal HashSet<object> BirthdayProjected { get; } = new(ReferenceEqualityComparer.Instance);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (BirthdayProjectionRequired
            && ChangeTracker.Entries<Contact>().FirstOrDefault(e => BirthdayProjector.IsRelevant(e) && !BirthdayProjected.Contains(e.Entity)) is { } missed)
        {
            throw new InvalidOperationException(
                $"Contact {missed.Entity.Id} is saved without its birthday projection: call IBirthdayProjector.ProjectTrackedAsync before SaveChanges.");
        }

        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        BirthdayProjected.Clear();
        return saved;
    }
```

(`BirthdayProjector` is `internal` in the same assembly; the context file gains `using weesky.Scotty.Microservice.Repositories;`.)

- [ ] **Step 5: Every contact write path**

`ContactStore`: primary constructor becomes `ContactStore(PreferencesDbContext context, IContactSyncStore sync, IBirthdayProjector? birthdays = null)` and gains

```csharp
    /// <summary>Called by every write path just before its SaveChanges (spec, décision 4).</summary>
    internal Task ProjectBirthdaysAsync(CancellationToken cancellationToken) =>
        birthdays?.ProjectTrackedAsync(cancellationToken) ?? Task.CompletedTask;
```

Insert `await ProjectBirthdaysAsync(cancellationToken);` immediately before the `SaveChangesAsync` that persists the contact change in: `CreateAsync` (line ~219), `UpdateAsync` transactional branch (~318), `DeleteAsync` (~356, after `context.Contacts.Remove(row)` and `StripFromGroupsAsync`), `DeleteManyAsync` (~454), `ImportAsync` and `ApplyMergesAsync` (every save inside the batch body, ~929), `BackfillAsync` (~971). In `DavContactWriter`, `await store.ProjectBirthdaysAsync(cancellationToken);` before the saves of `PutAsync` (~46 and ~89 of the method bodies), `DeleteAsync` (~107) and `DeleteAllAsync`.

`BackfillAsync` wraps its loop and save in `InTransactionAsync(async () => { …; return processed; }, cancellationToken)` — the `int` body always commits, as `DeleteManyAsync` does.

The guard is the checklist: run the whole suite at Step 7; any path missed throws with the contact id.

- [ ] **Step 6: Registration**

In `ApplicationServicesConfiguration`, beside the calendar stores: `services.AddScoped<IBirthdayProjector, BirthdayProjector>();`. The container fills `ContactStore`'s optional parameter since the service is registered.

- [ ] **Step 7: Run the new tests, then the whole suite**

Run: `dotnet test src/scotty.microservice.sln --filter "FullyQualifiedName~BirthdayProjectorTests|FullyQualifiedName~ContactBulkOperationTests"` → PASS.
Run: `dotnet test src/scotty.microservice.sln` → PASS (existing tests build `ContactStore` without projector, so the guard stays off for them).

- [ ] **Step 8: Commit**

```bash
git add src/scotty.microservice
git checkout -- src/scotty.microservice/ApiDocumentation.xml
git commit -F - <<'EOF'
Project contact birthdays into the birthdays calendar on every write

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 3: The calendar's life and the webmail API

**Files:**
- Modify: `src/scotty.microservice/Repositories/ICalendarStore.cs`, `CalendarStore.cs`, `CalendarBatchDelete.cs`
- Modify: `src/scotty.microservice/Models/Calendar/CalendarView.cs`, `CalendarWrite.cs`, `CalendarRequest.cs`, `EventOccurrence.cs`
- Create: `src/scotty.microservice/Models/Calendar/BirthdaysToggleRequest.cs`
- Modify: `src/scotty.microservice/Controllers/CalendarsController.cs`, `CalendarEventsController.cs`, `PreferencesController.cs`, `DavCredentialsController.cs:137`
- Modify: `src/scotty.microservice/Models/UserPreferences.cs`
- Modify: `src/scotty.microservice/Repositories/CalendarEventStore.cs` (read-only gate, occurrence stamping), `CalendarEventImporter.cs`
- Modify: `src/scotty.microservice/Configuration/ApplicationServicesConfiguration.cs` (nothing if `CalendarStore` stays constructor-injected)
- Test: `.../Repositories/CalendarStoreBirthdaysTests.cs`, `.../Repositories/CalendarEventStoreBirthdaysTests.cs`, `.../Controllers/CalendarsControllerBirthdaysTests.cs`, `.../Controllers/PreferencesControllerTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces:
  - `CalendarView(…, bool IsDefault, string Kind, string? BirthdayReminder)`; `CalendarWrite(…, string? TimeZone = null, string? BirthdayReminder = null)`; `CalendarRequest.BirthdayReminder`.
  - `ICalendarStore.EnsureBirthdaysAsync(Guid userId, string browserTimeZone, string language, CancellationToken) : Task`
  - `ICalendarStore.SetBirthdaysEnabledAsync(Guid userId, bool enabled, string browserTimeZone, string language, CancellationToken) : Task`
  - `ICalendarStore.SetBirthdayLanguageAsync(Guid userId, string language, CancellationToken) : Task`
  - `CalendarStore.ReadOnly = "The birthdays calendar is read-only"`, `CalendarStore.BirthdaysDavName = "birthdays"`, `CalendarStore.NotBirthdays = "Only the birthdays calendar has a reminder"`, `CalendarStore.BadReminder`.
  - `EventOccurrence(…, string? MyPartStat = null, Guid? ContactId = null, int? BirthYear = null)`.
  - `UserPreferences.CalendarBirthdays = "calendar.birthdays"`, `UserPreferences.IsManaged(string key)`.
  - Routes: `GET /api/Calendars?tz=&lang=`, `PUT /api/Calendars/Birthdays?tz=&lang=` body `{ "enabled": bool }` → 204.

- [ ] **Step 1: Write the failing tests**

`CalendarStoreBirthdaysTests.cs` (InMemory context, `TestCalendarSyncStore`, real `BirthdayProjector`, a real `UserPreferenceStore` on the same context):

```csharp
    [Fact] public async Task Ensure_creates_the_calendar_once_filled_with_the_birthdays()
    { /* 2 contacts with birthdays → EnsureBirthdaysAsync twice → one row Kind birthdays, DavName "birthdays",
         DisplayName "Anniversaires" for "fr", Color "#be185d", reminder same_day, language fr, Order after default, 2 events */ }
    [Fact] public async Task Ensure_does_nothing_while_the_preference_is_off() { }
    [Fact] public async Task Ensure_takes_birthdays_2_when_a_client_owns_birthdays() { }
    [Fact] public async Task The_language_is_ui_language_then_the_request_then_english()
    { /* ui.language "en" + request "fr" → en; ui.language "auto" + "fr" → fr; "auto" + "de" → en */ }
    [Fact] public async Task Disabling_removes_the_calendar_its_events_and_writes_no_revision() { }
    [Fact] public async Task Enabling_again_recreates_and_fills_it() { }
    [Fact] public async Task Deleting_it_through_the_api_is_refused() { }
    [Fact] public async Task Changing_the_reminder_rewrites_every_event_under_one_rank() { }
    [Fact] public async Task A_reminder_on_a_regular_calendar_is_refused() { }
    [Fact] public async Task An_unknown_reminder_is_refused() { }
    [Fact] public async Task Setting_the_same_language_writes_nothing_and_another_rewrites() { }
```

`CalendarEventStoreBirthdaysTests.cs`:

```csharp
    [Fact] public async Task Create_update_delete_and_move_into_birthdays_are_refused_read_only() { }
    [Fact] public async Task Import_into_birthdays_is_refused() { }
    [Fact] public async Task The_window_stamps_contact_and_birth_year_on_birthdays_only()
    { /* a birthdays event and a regular event carrying the same X-SCOTTY-CONTACT-ID line →
         only the first occurrence has ContactId and BirthYear 1986 */ }
    [Fact] public async Task A_browser_west_of_the_calendar_sees_the_birthday_on_its_own_date()
    { /* calendar Europe/Brussels, window 2026-06-20..23 read in America/Los_Angeles →
         occurrence StartDate 2026-06-21 */ }
    [Fact] public async Task Search_stamps_the_contact_too() { }
```

`CalendarsControllerBirthdaysTests.cs` (Moq `ICalendarStore`): `List` passes `lang` to `EnsureBirthdaysAsync`; `PUT Birthdays` with an unknown zone → `BadRequestObjectResult`; with `enabled:false` → `NoContentResult` and `SetBirthdaysEnabledAsync(…, false, …)`; `Import` into a birthdays calendar → 403 `ObjectResult` whose `StatusCode` is 403 (use `Assert.IsType<ObjectResult>` exactly, not `BadRequestObjectResult`).

`PreferencesControllerTests.cs` (existing file; add): `PUT calendar.birthdays` → `BadRequestObjectResult`; `PUT ui.language = en` → `SetBirthdayLanguageAsync(user, "en")` called once; `PUT ui.language = auto` → not called.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice.sln --filter "FullyQualifiedName~Birthdays|FullyQualifiedName~PreferencesControllerTests"`
Expected: build FAIL.

- [ ] **Step 3: Models and the registry**

`CalendarView` gains `string Kind, string? BirthdayReminder` after `IsDefault`; `CalendarStore.View` passes `row.Kind, row.BirthdayReminder`. `CalendarWrite` gains `string? BirthdayReminder = null`; `CalendarRequest` gains `public string? BirthdayReminder { get; set; }`; the controller's `Update` passes it. `EventOccurrence` gains `Guid? ContactId = null, int? BirthYear = null` after `MyPartStat`.

`BirthdaysToggleRequest.cs`: `public sealed class BirthdaysToggleRequest { public bool Enabled { get; set; } }`.

`UserPreferences`: `public const string CalendarBirthdays = "calendar.birthdays";`, registry entry `new(CalendarBirthdays, "on", ["on", "off"])`, and

```csharp
    /// <summary>Keys whose change has effects beyond the row: set through their own route, never PUT /api/Preferences.</summary>
    public static bool IsManaged(string key) => key == CalendarBirthdays;
```

- [ ] **Step 4: The store**

`CalendarStore` becomes `CalendarStore(PreferencesDbContext context, ICalendarSyncStore sync, IBirthdayProjector birthdays, IUserPreferenceStore preferences)` (update its three test constructions). New members:

```csharp
    internal const string BirthdaysDavName = "birthdays";
    internal const string ReadOnly = "The birthdays calendar is read-only";
    internal const string NotBirthdays = "Only the birthdays calendar has a reminder";
    internal const string BadReminder = "Unknown birthday reminder";
    private const string BirthdaysColour = "#be185d";

    public async Task EnsureBirthdaysAsync(Guid userId, string browserTimeZone, string language, CancellationToken cancellationToken)
    {
        var stored = UserPreferences.Effective(await preferences.GetAsync(userId, cancellationToken));
        if (stored[UserPreferences.CalendarBirthdays] == "off") return;
        if (await BirthdaysAsync(userId, cancellationToken) is not null) return;

        var chosen = BirthdayLanguages.Of(stored[UserPreferences.UiLanguage], language);
        try
        {
            await InTransactionAsync(async () =>
            {
                var held = await context.Calendars.AsNoTracking().Where(c => c.UserId == userId)
                    .Select(c => new { c.DavName, c.Order }).ToListAsync(cancellationToken);
                var row = new Calendar
                {
                    Id = Guid.NewGuid(), UserId = userId, Kind = CalendarKinds.Birthdays,
                    DavName = FreeName(held.Select(c => c.DavName)),
                    DisplayName = chosen == BirthdayLanguages.Fr ? "Anniversaires" : "Birthdays",
                    Description = string.Empty, Color = BirthdaysColour,
                    Order = held.Count == 0 ? 0 : held.Max(c => c.Order) + 1,
                    TimeZone = browserTimeZone, IsVisible = true,
                    BirthdayReminder = BirthdayReminders.Default, BirthdayLanguage = chosen,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                };
                context.Calendars.Add(row);
                await context.SaveChangesAsync(cancellationToken);
                await sync.CreateStateAsync(row.Id, cancellationToken);
                await birthdays.RebuildAsync(row, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first requests raced; ux_calendars_birthdays_owner kept one, which is what "ensure" owes.
            context.ChangeTracker.Clear();
            if (await BirthdaysAsync(userId, cancellationToken) is null) throw;
        }
    }

    public async Task SetBirthdaysEnabledAsync(Guid userId, bool enabled, string browserTimeZone, string language, CancellationToken cancellationToken)
    {
        await preferences.SetAsync(userId, UserPreferences.CalendarBirthdays, enabled ? "on" : "off", cancellationToken);
        if (enabled) { await EnsureBirthdaysAsync(userId, browserTimeZone, language, cancellationToken); return; }
        if (await BirthdaysAsync(userId, cancellationToken) is { } row) await RemoveAsync(userId, row, archive: false, cancellationToken);
    }

    public async Task SetBirthdayLanguageAsync(Guid userId, string language, CancellationToken cancellationToken)
    {
        if (await BirthdaysAsync(userId, cancellationToken) is not { } row || row.BirthdayLanguage == language) return;
        await InTransactionAsync(async () =>
        {
            row.BirthdayLanguage = language;
            row.UpdatedAt = DateTime.UtcNow;
            await birthdays.RebuildAsync(row, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    private Task<Calendar?> BirthdaysAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Calendars.FirstOrDefaultAsync(c => c.UserId == userId && c.Kind == CalendarKinds.Birthdays, cancellationToken);

    private static string FreeName(IEnumerable<string> taken)
    {
        var names = taken.ToHashSet();
        return Enumerable.Range(1, int.MaxValue)
            .Select(n => n == 1 ? BirthdaysDavName : $"{BirthdaysDavName}-{n}")
            .First(name => !names.Contains(name));
    }
```

`UpdateAsync`: after the colour check, before saving —

```csharp
        var rebuild = false;
        if (write.BirthdayReminder is { } reminder)
        {
            if (row.Kind != CalendarKinds.Birthdays) return Result.Failure(NotBirthdays);
            if (!BirthdayReminders.All.Contains(reminder)) return Result.Failure(BadReminder);
            rebuild = reminder != row.BirthdayReminder;
            row.BirthdayReminder = reminder;
        }
```

and when `rebuild`, the save runs as `InTransactionAsync(async () => { await birthdays.RebuildAsync(row, ct); await context.SaveChangesAsync(ct); return Result.Success(); }, ct)` instead of the bare save.

`DeleteAsync`: after the `NotDeletable` check add `if (row.Kind == CalendarKinds.Birthdays) return Result.Failure(ReadOnly);`, then move its body (from `var doomed` to the end) into `private async Task<Result> RemoveAsync(Guid userId, Calendar row, bool archive, CancellationToken)` and call it with `archive: true`. `CalendarBatchDelete.RunAsync` gains `bool archive` (before `cancellationToken`); when false it skips the `ArchiveAsync` loop. Every other caller passes `archive: true`.

`MapFailure` in `CalendarsController` and the `CalendarEventsController` map `CalendarStore.ReadOnly` to `StatusCode(StatusCodes.Status403Forbidden, ResultEnveloppe.CreateErrorEnveloppe(error))`; add that as `protected ActionResult ForbiddenEnveloppe(string message)` in `ApiBaseController` beside its siblings. `CalendarStore.NotBirthdays`/`BadReminder` stay 400; the API's `DELETE` on birthdays answers 400 like `default` (spec § 3).

- [ ] **Step 5: The read-only gate and the stamped occurrences**

In `CalendarEventStore`: `CreateAsync` after the calendar lookup, `UpdateAsync` after `source`/`target` (either birthdays), `DeleteAsync` after the calendar lookup: `if (calendar.Kind == CalendarKinds.Birthdays) return Result.Failure<EventWriteResult>(CalendarStore.ReadOnly);`. `CalendarEventImporter.ImportAsync` after its calendar lookup: `return new CalendarImportOutcome(0, 0, 0, 0, 1, [new(0, CalendarStore.ReadOnly)]);` for birthdays, and `CalendarsController.Import` checks `view.Kind` first and answers `ForbiddenEnveloppe(CalendarStore.ReadOnly)` before reading the file.

`ZonesAsync` becomes `KindsAndZonesAsync` returning `Dictionary<Guid, (string Zone, string Kind)>`; `WindowAsync` stamps:

```csharp
            var expanded = OccurrenceExpander.Expand(row.Id, row.CalendarId, parsed, fromUtc, toUtc,
                calendar.Zone, viewTimeZone);
            found.AddRange(calendar.Kind == CalendarKinds.Birthdays && BirthdayIcs.Identify(parsed) is { } birth
                ? expanded.Select(o => o with { ContactId = birth.ContactId, BirthYear = birth.BirthYear })
                : expanded);
```

`SearchAsync` applies the same stamp to the one occurrence it answers per event; share it as a private static `Stamp(EventOccurrence, IcsCalendar, string kind)` so the rule is written once.

- [ ] **Step 6: The controllers**

`CalendarsController.List(string tz, string? lang, …)`: after `EnsureDefaultAsync`, `await store.EnsureBirthdaysAsync(user, tz, lang ?? BirthdayLanguages.En, ct);`.

```csharp
    /// <summary>The birthdays calendar's switch (spec, décision 3): the preference, then the calendar.</summary>
    /// <response code="204">Saved</response>
    /// <response code="400">Unknown time zone</response>
    /// <response code="401">Not authenticated</response>
    [HttpPut("Birthdays")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> SetBirthdays(string tz, string? lang, BirthdaysToggleRequest request, CancellationToken cancellationToken)
    {
        if (!IcsTimeZones.IsKnownIana(tz)) return BadRequestEnveloppe(IcsTimeZones.UnknownZone);
        await store.SetBirthdaysEnabledAsync(AuthenticatedUser.WebmailUid, request.Enabled, tz, lang ?? BirthdayLanguages.En, cancellationToken);
        return NoContent();
    }
```

`PreferencesController` takes `ICalendarStore calendars`; refuses `UserPreferences.IsManaged(request.Key)` with `BadRequestEnveloppe($"'{request.Key}' is set through its own route")`; after `SetAsync`, `if (request.Key == UserPreferences.UiLanguage && request.Value is BirthdayLanguages.Fr or BirthdayLanguages.En) await calendars.SetBirthdayLanguageAsync(user, request.Value, ct);`.

`DavCredentialsController`: the `alongside` lambda also ensures the birthdays calendar:

```csharp
                alongside: async () =>
                {
                    await calendars.EnsureDefaultAsync(AuthenticatedUser.WebmailUid, toggle.TimeZone, cancellationToken);
                    await calendars.EnsureBirthdaysAsync(AuthenticatedUser.WebmailUid, toggle.TimeZone, BirthdayLanguages.En, cancellationToken);
                },
```

(match the delegate type `alongside` declares; `EnsureBirthdaysAsync` resolves `ui.language` first, so `En` is only the last fallback).

- [ ] **Step 7: Run the new tests, then the whole suite**

Run: `dotnet test src/scotty.microservice.sln --filter "FullyQualifiedName~Birthdays|FullyQualifiedName~PreferencesControllerTests"` → PASS.
Run: `dotnet test src/scotty.microservice.sln` → PASS.

- [ ] **Step 8: Commit**

```bash
git add src/scotty.microservice
git checkout -- src/scotty.microservice/ApiDocumentation.xml
git commit -F - <<'EOF'
Create, switch and tune the birthdays calendar; keep it read-only in the API

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 4: Read-only over CalDAV

**Files:**
- Modify: `src/scotty.microservice/Models/Calendar/DavCalendar.cs`, `Repositories/DavCalendarReader.cs:23-25`
- Modify: `src/scotty.microservice/Services/Dav/DavPropertyTables.cs:17-21,86-87`
- Modify: `src/scotty.microservice/Services/CalDav/CalDavProperties.cs` (calendar table, event table), `CalDavError.cs`
- Modify: `src/scotty.microservice/Controllers/CalDavController.cs` (`PutEventAsync`, `DeleteEventAsync`, `DeleteCalendarAsync`)
- Test: `.../Controllers/CalDavControllerBirthdaysTests.cs` (follow the arrange of the existing CalDAV controller tests), `.../Services/CalDav/CalDavPropertiesTests.cs`

**Interfaces:**
- Consumes: `CalendarKinds` (Task 1).
- Produces: `DavCalendar(…, string TimeZone, bool IsReadOnly = false)`; `DavPropertyTables.PrivilegeSet(bool readOnly = false, bool collection = false)`; `CalDavError.NeedPrivileges`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact] public async Task A_put_into_birthdays_is_403_need_privileges() { /* no writer call */ }
    [Fact] public async Task A_delete_of_a_birthday_is_403_need_privileges() { }
    [Fact] public async Task A_delete_of_the_birthdays_collection_is_403_need_privileges() { }
    [Fact] public async Task A_proppatch_of_the_name_and_colour_still_succeeds() { }

    [Fact]
    public void The_privileges_of_birthdays_leave_out_every_write_but_its_properties()
    {
        var collection = DavPropertyTables.PrivilegeSet(readOnly: true, collection: true);
        var member = DavPropertyTables.PrivilegeSet(readOnly: true);

        Assert.Equal(["read", "write-properties", "read-current-user-privilege-set"], Names(collection));
        Assert.Equal(["read", "read-current-user-privilege-set"], Names(member));
    }

    [Fact] public void Schedule_calendar_transp_is_transparent_on_birthdays_and_opaque_elsewhere() { }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice.sln --filter "FullyQualifiedName~CalDavControllerBirthdaysTests|FullyQualifiedName~CalDavPropertiesTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`DavCalendar` gains `bool IsReadOnly = false`; `DavCalendarReader.ToCalendar` passes `c.Kind == CalendarKinds.Birthdays`.

`DavPropertyTables`:

```csharp
    private static readonly string[] ReadOnlyPrivileges = ["read", "read-current-user-privilege-set"];

    internal static XElement PrivilegeSet(bool readOnly = false, bool collection = false) =>
        new(DavXml.Dav + "current-user-privilege-set",
            (readOnly ? (collection ? ["read", "write-properties", "read-current-user-privilege-set"] : ReadOnlyPrivileges) : Privileges)
                .Select(p => new XElement(DavXml.Dav + "privilege", new XElement(DavXml.Dav + p))));
```

`CalDavProperties`: the calendar table's entry becomes `r => PrivilegeSet(r.Calendar?.IsReadOnly == true, collection: true)`, the event table's `r => PrivilegeSet(r.Calendar?.IsReadOnly == true)`; the calendar table adds

```csharp
        (DavXml.CalDav + "schedule-calendar-transp", r => new XElement(DavXml.CalDav + "schedule-calendar-transp",
            new XElement(DavXml.CalDav + (r.Calendar?.IsReadOnly == true ? "transparent" : "opaque")))),
```

`CalDavError`: `internal static readonly XName NeedPrivileges = DavXml.Dav + "need-privileges";`.

`CalDavController`: right after `FindCalendarOr404Async` in `PutEventAsync`, `DeleteEventAsync` and `DeleteCalendarAsync`:

```csharp
            if (calendar.IsReadOnly)
            {
                await RefuseAsync(trace, CalDavError.NeedPrivileges, null, cancellationToken);
                return;
            }
```

(In `PutEventAsync` this comes before the body is read, so a refused PUT archives nothing.)

- [ ] **Step 4: Run the new tests, then the whole suite**

Run: `dotnet test src/scotty.microservice.sln` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/scotty.microservice
git checkout -- src/scotty.microservice/ApiDocumentation.xml
git commit -F - <<'EOF'
Serve the birthdays calendar read-only over CalDAV

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 5: The calendar screen

**Files:**
- Modify: `src/frontend/src/modules/calendar/calendarTypes.ts`, `src/frontend/src/api.ts:284-302`, `queries.ts`, `useCalendarWrites.ts`
- Modify: `CalendarSidebar.tsx`, `CalendarDialog.tsx`, `CalendarDialogs.tsx`, `EventPreview.tsx`, `CalendarLayout.tsx`, `useDragEvent.ts`, `useResizeEvent.ts`, `ImportDialog.tsx`
- Create: `src/frontend/src/modules/calendar/birthday.ts`, `BirthdayScreen.tsx`
- Modify: `src/frontend/src/locales/{fr,en}/calendar.json`
- Test: `birthday.test.ts`, `BirthdayScreen.test.tsx`, and additions to `CalendarSidebar.test.tsx`, `CalendarDialog.test.tsx`, `EventPreview.test.tsx`, `CalendarLayout.test.tsx`, `useDragEvent.test.ts`, `useResizeEvent.test.ts`, `api.test.ts`

**Interfaces:**
- Consumes: the API of Task 3 (`kind`, `birthdayReminder`, `contactId`, `birthYear`, `lang` on `GET /api/Calendars`).
- Produces: `Calendar.kind: 'regular' | 'birthdays'`, `Calendar.birthdayReminder?: BirthdayReminder`; `CalendarWrite.birthdayReminder?`; `Occurrence.contactId?: string`, `Occurrence.birthYear?: number`; `api.getCalendars(tz, lang)`, `api.setBirthdays(enabled, tz, lang)`; `useSetBirthdays()`; `isBirthday(o)`, `ageOf(o)`, `contactUrlOf(id)`, `reminderLabel(value, t)`, `reminderLine(value, t)`, `REMINDER_OPTIONS` in `birthday.ts`.

- [ ] **Step 1: Write the failing tests**

`birthday.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { ageOf, contactUrlOf, isBirthday } from './birthday'
import type { Occurrence } from './calendarTypes'

const base = { eventId: 'e', calendarId: 'c', uid: 'u', instanceId: '20260621', isOverride: false,
  isAllDay: true, isFloating: false, startDate: '2026-06-21', endDateExclusive: '2026-06-22',
  transparency: 'TRANSPARENT', hasAlarm: true } satisfies Occurrence

describe('birthday', () => {
  it('is a birthday only when the server stamped a contact', () => {
    expect(isBirthday(base)).toBe(false)
    expect(isBirthday({ ...base, contactId: 'k' })).toBe(true)
  })
  it('counts the age at the occurrence shown', () => {
    expect(ageOf({ ...base, contactId: 'k', birthYear: 1986 })).toBe(40)
    expect(ageOf({ ...base, contactId: 'k', startDate: '2027-06-21', birthYear: 1986 })).toBe(41)
  })
  it('has no age without a year', () => {
    expect(ageOf({ ...base, contactId: 'k' })).toBeNull()
  })
  it('opens the contact the way the contacts module selects one', () => {
    expect(contactUrlOf('k')).toBe('/contacts?id=k')
  })
})
```

Component tests (Testing Library, existing harness `calendarTestHarness.tsx`):
- `CalendarSidebar`: a birthdays calendar's menu lists exactly `Settings…`, `Export`, `Disable…` (English locale in tests) and no `Import…`/`Delete…`; a regular one keeps its five entries.
- `CalendarDialog` with `reminder` set: renders a `Reminder` `MenuSelect` with the four options; submitting sends `birthdayReminder`.
- `EventPreview` on a birthday occurrence: shows `40 years old` (fr: `40 ans`), `Reminder the day before at 9:00`, one button `Open card`, no `Edit`/`Delete`; the year-less one shows no age line; a `none` reminder shows no bell line.
- `BirthdayScreen`: header `Birthday`, the same lines, `Open card` navigates to `/contacts?id=k`, ✕ calls `onClose`.
- `CalendarLayout`: on phone a tap on a birthday opens `BirthdayScreen`, not the editor; on desktop `onOpenEditor` for a birthday navigates to the card; the editor's calendar select never lists the birthdays calendar; the Disable confirm calls `api.setBirthdays(false, tz, lang)`.
- `useDragEvent`/`useResizeEvent`: `onPointerDown` on a birthday occurrence starts nothing.
- `api.test.ts`: `getCalendars('Europe/Brussels', 'fr')` requests `/api/Calendars?tz=Europe%2FBrussels&lang=fr`; `setBirthdays(false, tz, 'fr')` sends `PUT /api/Calendars/Birthdays?tz=…&lang=fr` with `{ enabled: false }`.

- [ ] **Step 2: Run to verify they fail**

Run (from `src/frontend`): `npx vitest run src/modules/calendar src/api.test.ts`
Expected: FAIL (missing module `./birthday`, missing props).

- [ ] **Step 3: Types, API, queries**

`calendarTypes.ts`:

```ts
export type CalendarKind = 'regular' | 'birthdays'
export type BirthdayReminder = 'none' | 'same_day' | 'day_before' | 'week_before'
```

`Calendar` gains `kind: CalendarKind` and `birthdayReminder?: BirthdayReminder`; `CalendarWrite` gains `birthdayReminder?: BirthdayReminder`; `Occurrence` gains `contactId?: string` and `birthYear?: number` (optional: the API omits nulls).

`api.ts`: `getCalendars: (tz: string, lang: string) => request<CalendarListResponse>('GET', \`/api/Calendars?tz=${encodeURIComponent(tz)}&lang=${encodeURIComponent(lang)}\`)`; `setBirthdays: (enabled: boolean, tz: string, lang: string) => request<null>('PUT', \`/api/Calendars/Birthdays?tz=${encodeURIComponent(tz)}&lang=${encodeURIComponent(lang)}\`, { enabled })`.

`queries.ts`: `useCalendars` passes `i18next.language`; add

```ts
/** The birthdays switch: the calendar appears or goes, and the preference with it. */
export function useSetBirthdays() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ enabled, tz }: { enabled: boolean; tz: string }) =>
      api.setBirthdays(enabled, tz, i18next.language),
    onSettled: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: calendarKeys.all(accountId) }),
      queryClient.invalidateQueries({ queryKey: ['preferences'] }),
    ]),
  })
}
```

(use the preferences key the settings module already exports rather than the literal, if one exists).

- [ ] **Step 4: `birthday.ts`**

```ts
import type { TFunction } from 'i18next'
import type { BirthdayReminder, Occurrence } from './calendarTypes'

export const REMINDER_OPTIONS: BirthdayReminder[] = ['none', 'same_day', 'day_before', 'week_before']

export const isBirthday = (o: Occurrence): boolean => o.contactId !== undefined

/** The age reached on the occurrence shown; null when the card holds no year. */
export function ageOf(o: Occurrence): number | null {
  if (o.birthYear === undefined || !o.startDate) return null
  return Number(o.startDate.slice(0, 4)) - o.birthYear
}

export const contactUrlOf = (id: string): string => `/contacts?id=${encodeURIComponent(id)}`

/** Literal keys, one per value: the i18n rule forbids building a key from a variable. */
export function reminderLabel(value: BirthdayReminder, t: TFunction<'calendar'>): string {
  switch (value) {
    case 'none': return t('dialogs.reminders.none')
    case 'same_day': return t('dialogs.reminders.same_day')
    case 'day_before': return t('dialogs.reminders.day_before')
    case 'week_before': return t('dialogs.reminders.week_before')
  }
}

/** The bubble's bell line; never asked for `none`, which shows no line. */
export function reminderLine(value: Exclude<BirthdayReminder, 'none'>, t: TFunction<'calendar'>): string {
  switch (value) {
    case 'same_day': return t('preview.reminderAt.same_day')
    case 'day_before': return t('preview.reminderAt.day_before')
    case 'week_before': return t('preview.reminderAt.week_before')
  }
}
```

(`contactUrlOf` uses the contacts module's own URL builder if `contactsUrl` is exported; otherwise this one line is the single place the shape is written.)

- [ ] **Step 5: Sidebar, dialog, disable**

`CalendarSidebar`: new props `onSettings(calendar)` and `onDisable(calendar)`; the items are chosen by kind:

```tsx
items={one.kind === 'birthdays'
  ? [
      { label: t('sidebar.settings'), onSelect: () => onSettings(one) },
      { label: t('sidebar.export'), onSelect: () => onExport(one) },
      'separator',
      { label: t('sidebar.disable'), onSelect: () => onDisable(one) },
    ]
  : [ /* the five existing entries, unchanged */ ]}
```

`CalendarDialog` gains optional `initialReminder?: BirthdayReminder`; when set, a third row under the colour fields:

```tsx
{reminder !== undefined && (
  <div className="field-h">
    <span className="field-h-label" id="calendar-reminder-label">{t('dialogs.reminder')}</span>
    <MenuSelect ariaLabel={t('dialogs.reminder')} value={reminder} onChange={v => setReminder(v as BirthdayReminder)}
      options={REMINDER_OPTIONS.map(value => ({ value, label: reminderLabel(value, t) }))} />
  </div>
)}
<p className="modal-hint">{t('dialogs.reminderHint')}</p>  {/* only with the row */}
```

`CalendarValues` gains `birthdayReminder?: BirthdayReminder`, passed through `saveCalendar` into `updateCalendar`.

`useCalendarWrites`: `Editing` gains `{ mode: 'settings'; calendar: Calendar }`; add `disabling` state (`Calendar | null`), `setDisabling`, and `confirmDisable()` which calls `useSetBirthdays().mutateAsync({ enabled: false, tz })`, toasting `errors.calendarSave` on failure. `CalendarDialogs` opens `CalendarDialog` with `initialReminder={editing.calendar.birthdayReminder}` for `settings`, and renders the disable confirm:

```tsx
{writes.disabling && (
  <DeleteConfirmModal title={t('dialogs.disableTitle')} message={t('dialogs.disableBody')}
    confirmLabel={t('dialogs.disable')} loading={writes.disablingBirthdays}
    onConfirm={() => void writes.confirmDisable()} onClose={() => writes.setDisabling(null)}
    returnFocusRef={returnFocusRef} />
)}
```

If `DeleteConfirmModal` only draws a danger button, add a `tone?: 'danger' | 'primary'` prop there (default `danger`) rather than a second modal: the spec wants a primary button because nothing is lost.

- [ ] **Step 6: Preview, phone screen, routing, gestures, editor**

`EventPreview`: `const birthday = isBirthday(occurrence)`. When `birthday`: skip `useEvent` (pass `null`), render after the `when` line

```tsx
{ageOf(occurrence) !== null && (
  <p className="event-preview-row is-strong"><CakeIcon size={14} />{t('preview.age', { count: ageOf(occurrence)! })}</p>
)}
{calendar?.birthdayReminder && calendar.birthdayReminder !== 'none' && (
  <p className="event-preview-row"><BellIcon size={14} />{reminderLine(calendar.birthdayReminder, t)}</p>
)}
<p className="event-preview-row"><RepeatIcon size={14} />{t('preview.yearly')}</p>
```

then the calendar row, and one action `<button className="btn btn-primary" onClick={onOpenContact}><UserIcon size={14} />{t('preview.openCard')}</button>` instead of Edit/Delete. New prop `onOpenContact`. Add `.event-preview-row.is-strong { color: var(--text); }` to `styles/calendar.css`. Create `src/frontend/src/icons/CakeIcon.tsx` on the model of `BellIcon.tsx` with the lucide cake paths used on the mockup board.

`BirthdayScreen.tsx`: the phone's read-only screen (`calendar-editor-screen` container, `role="dialog" aria-modal="true"`, header `calendar-editor-head` with the title `t('preview.birthdayTitle')` and the ✕ `modal-close`), then the same title/when/lines as the preview (extract the lines into a `BirthdayLines` component used by both, so they are written once), and a full-width `btn btn-primary` `Open card`. Focus the ✕ on mount; Escape closes (use `useLayer`/`useDismiss` as the editor screen does).

`CalendarLayout`:

```tsx
const openContact = useCallback((one: Occurrence) => navigate(contactUrlOf(one.contactId!)), [navigate])
const [birthdayShown, setBirthdayShown] = useState<Occurrence | null>(null)

const openFromChip = useCallback((one: Occurrence) => {
  if (isBirthday(one)) { if (phone) setBirthdayShown(one); else openContact(one); return }
  openEditor(one.eventId, one.instanceId || undefined)
}, [openEditor, phone, openContact])
```

`EventPreview` receives `onOpenContact={() => openContact(preview.occurrence)}`; render `{birthdayShown && <BirthdayScreen … onClose={() => setBirthdayShown(null)} onOpenContact={() => openContact(birthdayShown)} />}`. The sidebar gets `onSettings={c => calendarWrites.setEditing({ mode: 'settings', calendar: c })}` and `onDisable={calendarWrites.setDisabling}`. The editor receives `calendars={calendars.filter(c => c.kind !== 'birthdays')}`; check `useEditorSeed`'s default calendar pick and `ImportDialog`'s target list and filter the same way.

`useDragEvent.onPointerDown` and `useResizeEvent.onPointerDown`: first line `if (isBirthday(o)) return`.

- [ ] **Step 7: Strings**

`en/calendar.json` / `fr/calendar.json` (French with `’` and ` `):

| key | en | fr |
|---|---|---|
| `sidebar.settings` | Settings… | Réglages… |
| `sidebar.disable` | Disable… | Désactiver… |
| `dialogs.reminder` | Reminder | Rappel |
| `dialogs.reminders.none` | None | Aucun |
| `dialogs.reminders.same_day` | On the day at 9:00 | Le jour même à 9:00 |
| `dialogs.reminders.day_before` | The day before at 9:00 | La veille à 9:00 |
| `dialogs.reminders.week_before` | A week before | Une semaine avant |
| `dialogs.reminderHint` | For every birthday, on all your synced devices. | Pour chaque anniversaire, sur tous vos appareils synchronisés. |
| `dialogs.disableTitle` | Turn off birthdays? | Désactiver les anniversaires ? |
| `dialogs.disableBody` | Birthdays will disappear from the webmail and your devices. Your contacts are not changed. You can turn this calendar back on in Settings › General. | Les anniversaires disparaîtront du webmail et de vos appareils. Vos contacts ne sont pas modifiés. Vous pourrez réactiver cet agenda dans Paramètres › Général. |
| `dialogs.disable` | Disable | Désactiver |
| `preview.age_one` / `_other` | {{count}} year old / {{count}} years old | {{count}} an / {{count}} ans |
| `preview.reminderAt.same_day` | Reminder on the day at 9:00 | Rappel le jour même à 9:00 |
| `preview.reminderAt.day_before` | Reminder the day before at 9:00 | Rappel la veille à 9:00 |
| `preview.reminderAt.week_before` | Reminder a week before | Rappel une semaine avant |
| `preview.yearly` | Every year | Chaque année |
| `preview.openCard` | Open card | Ouvrir la fiche |
| `preview.birthdayTitle` | Birthday | Anniversaire |

- [ ] **Step 8: Run, typecheck, lint**

Run (from `src/frontend`): `npx vitest run src/modules/calendar src/api.test.ts` → PASS; `npm run typecheck` → no error; `npm run lint` → no error.

- [ ] **Step 9: Commit**

```bash
git add src/frontend
git commit -F - <<'EOF'
Show birthdays in the calendar: read-only bubble, phone screen, settings and disable

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 6: The switch in Settings › General

**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx`
- Modify: the preferences accessor module that defines `PREFERENCE_KEYS` (add `calendarBirthdays: 'calendar.birthdays'` and a `birthdaysOnOf(preferences)` accessor beside `showPreviewOf`)
- Modify: `src/frontend/src/locales/{fr,en}/settings.json`
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: `useSetBirthdays` (Task 5), `ToggleRow`.

- [ ] **Step 1: Write the failing test**

```tsx
it('switches the birthdays calendar through its own route', async () => {
  // preferences answer calendar.birthdays = 'on'
  render(<GeneralPage />, { wrapper })
  const toggle = await screen.findByRole('checkbox', { name: 'Birthdays calendar' })
  expect(toggle).toBeChecked()

  await userEvent.click(toggle)

  expect(api.setBirthdays).toHaveBeenCalledWith(false, expect.any(String), 'en')
  expect(api.setPreference).not.toHaveBeenCalledWith('calendar.birthdays', expect.anything())
  expect(await screen.findByText('Birthdays calendar turned off')).toBeInTheDocument()
})
```

plus one asserting the section heading `Calendar` sits between `Layout` and `Privacy`.

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/modules/settings/general/GeneralPage.test.tsx` → FAIL.

- [ ] **Step 3: Implement**

A new `<section className="account-section">` between Layout's and Privacy's:

```tsx
<section className="account-section">
  <h2>{t('general.calendar')}</h2>
  <ToggleRow
    id="birthdays-calendar"
    label={t('general.birthdays.label')}
    hint={t('general.birthdays.hint')}
    checked={birthdaysOnOf(preferences)}
    disabled={setBirthdays.isPending}
    onChange={on => setBirthdays.mutate(
      { enabled: on, tz: Intl.DateTimeFormat().resolvedOptions().timeZone },
      {
        onSuccess: () => addToast(t(on ? 'general.birthdays.on' : 'general.birthdays.off'), 'success'),
        onError: error => addToast(apiErrorMessage(error, t('general.saveFailed')), 'error'),
      })}
  />
</section>
```

(Use the browser-zone helper the calendar module already uses for `tz` if one exists, rather than a second `Intl` call.)

Strings — `general.calendar`: Calendar / Agenda; `general.birthdays.label`: Birthdays calendar / Agenda des anniversaires; `.hint`: Your contacts’ birthdays, in the webmail and on your synced devices. / Les dates de naissance de vos contacts, dans le webmail et sur vos appareils synchronisés.; `.on`: Birthdays calendar turned on / Agenda des anniversaires activé; `.off`: Birthdays calendar turned off / Agenda des anniversaires désactivé.

- [ ] **Step 4: Run the whole frontend suite, typecheck, lint**

Run: `npx vitest run` → PASS (the intermittent chunk-budget test classed in memory may flake; rerun it alone before treating it as a failure); `npm run typecheck`; `npm run lint`.

- [ ] **Step 5: Commit**

```bash
git add src/frontend
git commit -F - <<'EOF'
Add the birthdays calendar switch to Settings › General

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

## After the six tasks

Whole-branch review, then the manual acceptance run of the spec's « Recette » (iPhone, Android + DAVx⁵, Thunderbird) on the dev host after the SQL of `docs/operations/birthdays-calendar-migration.md` is replayed there. Nothing is pushed without the user's go.
