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
            if (await BirthdaysOfAsync(context, byUser.Key, cancellationToken) is not { } calendar) continue;

            var wanted = byUser.ToDictionary(c => BirthdayIcs.DavNameOf(c.Entity.Id),
                c => c.Deleted ? null : Compose(c.Entity, calendar));
            var names = wanted.Keys.ToList();
            var held = await context.CalendarEvents
                .Where(e => e.CalendarId == calendar.Id && names.Contains(e.DavName))
                .ToDictionaryAsync(e => e.DavName, cancellationToken);
            await ApplyAsync(calendar, wanted, held, null, cancellationToken);
        }
    }

    public async Task RebuildAsync(Calendar calendar, CancellationToken cancellationToken)
    {
        // The lock before the reads: a contact write committing in between would otherwise be undone,
        // or collide on (calendar_id, dav_name). An aligned rebuild spends that rank for nothing.
        var rank = await sync.NextSequenceAsync(calendar.Id, cancellationToken);
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

        await ApplyAsync(calendar, wanted, held, rank, cancellationToken);
    }

    /// <summary>The user's birthdays collection, tracked; <see cref="CalendarStore"/> reads it through here too.</summary>
    internal static Task<Calendar?> BirthdaysOfAsync(PreferencesDbContext context, Guid userId, CancellationToken cancellationToken) =>
        context.Calendars.FirstOrDefaultAsync(
            c => c.UserId == userId && c.Kind == CalendarKinds.Birthdays, cancellationToken);

    private static string? Compose(Contact contact, Calendar calendar) =>
        BirthdayIcs.Compose(contact, calendar.BirthdayReminder ?? BirthdayReminders.Default,
            calendar.BirthdayLanguage ?? BirthdayLanguages.En, DateTime.UtcNow);

    /// <summary>One rank for the whole change: <paramref name="taken"/> when the caller already holds it,
    /// else one taken here, and none when nothing differs from what is stored. A collection removed
    /// since it was read (the switch turned off meanwhile) takes no event.</summary>
    private async Task ApplyAsync(Calendar calendar, Dictionary<string, string?> wanted,
        Dictionary<string, CalendarEvent> held, ulong? taken, CancellationToken cancellationToken)
    {
        var changes = wanted.Where(w => !Same(held.GetValueOrDefault(w.Key), w.Value)).ToList();
        if (changes.Count == 0) return;

        if ((taken ?? await sync.NextSequenceIfPresentAsync(calendar.Id, cancellationToken)) is not { } rank) return;
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
