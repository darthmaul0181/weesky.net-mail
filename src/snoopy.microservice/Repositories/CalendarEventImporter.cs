using System.Text;
using Ical.Net.Serialization;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The import and export half of <see cref="CalendarEventStore"/>, split out so neither file has to
/// be read whole to follow the other. It writes through the store's own gate — its transaction
/// wrapper, its <c>ApplyIcsAsync</c> — over the very same context, and owns no rule of its own.
/// </summary>
internal sealed class CalendarEventImporter(
    CalendarEventStore store, PreferencesDbContext context, ICalendarSyncStore sync)
{
    private const string ProductId = "-//weesky//webmail//EN";
    private const string IcsVersion = "2.0";

    /// <summary>What a component's UID line is inserted after when it declares none.</summary>
    private const string EventBegin = "BEGIN:VEVENT";

    public async Task<CalendarImportOutcome> ImportAsync(
        Guid userId, Guid calendarId, string vcalendar, CancellationToken cancellationToken)
    {
        // Judged before a single byte is parsed: parsing is the work an oversized body is trying to
        // make us do. Line 0 is the file itself, not a resource in it.
        if (Encoding.UTF8.GetByteCount(vcalendar) > CalendarEventStore.MaxImportBytes)
            return new CalendarImportOutcome(0, 0, 0, 0, 1, [new(0, CalendarEventStore.FileTooLarge)]);

        var calendar = await store.FindCalendarAsync(userId, calendarId, cancellationToken);
        if (calendar is null)
            return new CalendarImportOutcome(0, 0, 0, 0, 1, [new(0, CalendarStore.NotFound)]);

        var split = IcsResources.Split(vcalendar);
        var errors = new List<ContactImportError>();
        int created = 0, replaced = 0, failed = 0;

        // A hundred resources per transaction, one rank each: every replacement archives what it
        // overwrites, so one transaction for a whole file would write gigabytes of MEDIUMTEXT — a
        // redo log that overflows, and the state row's lock held long enough to time clients out.
        // The line carried on an error is the resource's 1-based rank in this split, not a line of
        // the file: grouping by UID goes through the object model, which loses the file's own.
        foreach (var chunk in split.Resources.Select((Ics, Rank) => (Ics, Line: Rank + 1)).Chunk(CalendarEventStore.ImportBatch))
        {
            var batch = await store.InTransactionAsync(
                () => ImportBatchAsync(userId, calendar, chunk, cancellationToken), cancellationToken);

            created += batch.Created;
            replaced += batch.Replaced;
            failed += batch.Failed;
            errors.AddRange(batch.Errors);
        }

        return new CalendarImportOutcome(
            created, replaced, split.IgnoredTodos, split.IgnoredJournals, failed, errors);
    }

    /// <summary>
    /// One transaction's worth. Reports its refusals inside the outcome rather than aborting — the
    /// return type is no <c>Result</c>, so the wrapper always commits what succeeded.
    /// </summary>
    private async Task<CalendarImportOutcome> ImportBatchAsync(
        Guid userId, Calendar calendar, IReadOnlyList<(string Ics, int Line)> resources,
        CancellationToken cancellationToken)
    {
        // The state row's lock FIRST, before any resource is looked at, so that every door of these
        // two stores locks in one order and none of them can deadlock against another.
        var rank = await sync.NextSequenceAsync(calendar.Id, cancellationToken);

        // Recounted per batch: five hundred resources landing in a collection that already holds
        // 4 800 must stop at 5 000 rather than spend a total an initial count believed free.
        var stored = await context.CalendarEvents
            .CountAsync(e => e.CalendarId == calendar.Id, cancellationToken);

        var errors = new List<ContactImportError>();
        int created = 0, replaced = 0, failed = 0;

        foreach (var (text, line) in resources)
        {
            var id = Guid.NewGuid();
            // The plan's one allowed text surgery, and it runs BEFORE the parse: a resource stored
            // verbatim must already carry the identity a CalDAV client syncs on, and synthesising
            // one at serving time would divorce the bytes served from the bytes hashed.
            var ics = WithUid(text, id.ToString());

            var parsed = CalendarEventStore.Parse(ics);
            if (parsed.IsFailure)
            {
                failed++;
                errors.Add(new ContactImportError(line, parsed.Error));
                continue;
            }

            // Read off the component, not off a whole projection: ApplyIcsAsync projects again a few
            // lines below, and the UID is the one field a projection costs nothing to skip.
            var uid = IcsDocument.MasterOf(parsed.Value)?.Uid
                      ?? IcsDocument.Components(parsed.Value).First().Uid ?? string.Empty;
            var held = await context.CalendarEvents.FirstOrDefaultAsync(
                e => e.CalendarId == calendar.Id && e.Uid == uid, cancellationToken);

            if (held is null && stored + created >= CalendarEventStore.MaxPerCalendar)
            {
                failed++;
                errors.Add(new ContactImportError(line, CalendarEventStore.CapReached));
                continue;
            }

            if (held is null)
            {
                held = new CalendarEvent
                {
                    Id = id, CalendarId = calendar.Id, UserId = userId, DavName = $"{id}.ics"
                };
                context.CalendarEvents.Add(held);
                created++;
            }
            else
            {
                // Replaced whole, not merged: the file's resource IS the event (fonctionnalité 6),
                // and the bytes it displaces are archived in the same transaction as the write.
                await sync.ArchiveAsync(
                    userId, calendar.Id, held.Id, held.Uid, held.DavName, held.IcsRaw,
                    RevisionCause.Import, cancellationToken);
                replaced++;
            }

            await store.ApplyIcsAsync(held, calendar, ics, parsed.Value, rank, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        return new CalendarImportOutcome(created, replaced, 0, 0, failed, errors);
    }

    public async Task<string> ExportAsync(
        Guid userId, Guid calendarId, CancellationToken cancellationToken)
    {
        var calendar = await store.FindCalendarAsync(userId, calendarId, cancellationToken);
        if (calendar is null) return string.Empty;

        var rows = await context.CalendarEvents.AsNoTracking()
            .Where(e => e.CalendarId == calendarId)
            .OrderBy(e => e.FirstOccurrence)
            .Select(e => e.IcsRaw)
            .ToListAsync(cancellationToken);

        var file = new IcsCalendar { ProductId = ProductId, Version = IcsVersion };
        // RFC 7986 for the readers that know it, the two X- lines for Apple and Google, which do
        // not: a file that arrives unnamed lands as "Untitled" in every one of them.
        // Stripped of its breaks: an X- property is written raw, and a name carrying a CRLF would
        // forge iCalendar lines in a file the user then hands to another client.
        var name = OneLine(calendar.DisplayName);
        file.Properties.Set("NAME", name);
        file.Properties.Set("X-WR-CALNAME", name);
        file.Properties.Set("COLOR", OneLine(calendar.Color));
        file.Properties.Set("X-APPLE-CALENDAR-COLOR", OneLine(calendar.Color));

        var zones = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stored in rows)
        {
            if (IcsDocument.TryLoad(stored) is not { } parsed) continue;

            foreach (var zone in parsed.TimeZones.ToList())
                if (zone?.TzId is { Length: > 0 } tzid && zones.Add(tzid)) file.TimeZones.Add(zone);

            // Every component of the resource, overrides included: dropping them would export a
            // series whose exceptions have silently gone back to the rule.
            foreach (var component in IcsDocument.Components(parsed).ToList())
                file.Events.Add(component);
        }

        return new CalendarSerializer().SerializeToString(file) ?? string.Empty;
    }

    /// <summary>
    /// The resource with a <c>UID</c> equal to <paramref name="uid"/>, inserted right after the
    /// first <c>BEGIN:VEVENT</c>, when — and only when — the component declares none. Textual,
    /// never a re-serialisation: the stored bytes are the ETag, and re-emitting them through the
    /// library would change them for nothing. Mirrors <see cref="ContactStore.WithUid"/>.
    /// </summary>
    internal static string WithUid(string ics, string uid)
    {
        var begin = ics.IndexOf(EventBegin, StringComparison.OrdinalIgnoreCase);
        if (begin < 0 || DeclaresUid(ics, begin)) return ics;

        var lineBreak = ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var after = ics.IndexOf('\n', begin);
        if (after < 0) return ics + lineBreak + "UID:" + OneLine(uid);
        return string.Concat(ics[..(after + 1)], "UID:", OneLine(uid), lineBreak, ics[(after + 1)..]);
    }

    /// <summary>Whether the first component carries a UID line of its own. Unfolded and stopped at
    /// its own END, so a folded property and a later component cannot answer for it.</summary>
    private static bool DeclaresUid(string ics, int begin)
    {
        var unfolded = ics[begin..]
            .Replace("\r\n", "\n").Replace("\n ", string.Empty).Replace("\n\t", string.Empty);
        foreach (var line in unfolded.Split('\n'))
        {
            if (line.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase)) return false;

            var colon = line.IndexOf(':');
            if (colon <= 0 || line.AsSpan(colon + 1).Trim().Length == 0) continue;
            var name = line.AsSpan(0, colon);
            var semicolon = name.IndexOf(';');
            if (semicolon >= 0) name = name[..semicolon];
            if (name.Trim().Equals("UID", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>A break in a value is stripped rather than assumed away: injected, it would forge
    /// a line or end the resource.</summary>
    private static string OneLine(string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
