using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The one batch both deletions of a calendar's resources run: the collection removed from the
/// sidebar (<see cref="CalendarStore.DeleteAsync"/>) and the <c>default</c> one emptied by a DAV
/// client (<see cref="DavCalendarWriter.DeleteAllAsync"/>). Each caller opens its own transaction
/// around it — theirs are not the same door — and says whether a tombstone is owed per resource.
/// </summary>
internal static class CalendarBatchDelete
{
    /// <param name="context">the preferences context both stores share</param>
    /// <param name="sync">the counter every rank of this calendar is cut from</param>
    /// <param name="userId">the owner the archive rows are written for</param>
    /// <param name="calendarId">the collection being emptied</param>
    /// <param name="ids">one batch of resource ids, read before the transaction opened</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <param name="tombstones">
    /// True when the collection SURVIVES: a client that keeps it learns of each resource by name,
    /// where one losing the collection loses everything under it without being told name by name.
    /// </param>
    internal static async Task<int> RunAsync(PreferencesDbContext context, ICalendarSyncStore sync,
        Guid userId, Guid calendarId, List<Guid> ids, bool tombstones,
        CancellationToken cancellationToken)
    {
        // The state row's lock FIRST, as every other transaction of these two stores takes it, so
        // no door of theirs can deadlock against another.
        var rank = await sync.NextSequenceAsync(calendarId, cancellationToken);

        // Read under the lock, so what is archived is what is being removed.
        var batch = await context.CalendarEvents
            .Where(e => e.CalendarId == calendarId && ids.Contains(e.Id))
            .ToListAsync(cancellationToken);

        foreach (var stored in batch)
        {
            // EventId NULL: a delete revision outlives the row it describes, and CalendarId
            // survives on purpose — calendar_revisions carries no FK, so the archive is not
            // cascaded away by the very deletion that wrote it (décision 2).
            await sync.ArchiveAsync(userId, calendarId, null, stored.Uid, stored.DavName,
                stored.IcsRaw, RevisionCause.Delete, cancellationToken);
        }

        // The InMemory provider enforces no foreign key, so the children go by hand: this is what
        // makes it behave like the cascade MariaDB actually runs.
        context.CalendarAttendees.RemoveRange(
            await context.CalendarAttendees.Where(a => ids.Contains(a.EventId))
                .ToListAsync(cancellationToken));
        context.CalendarEvents.RemoveRange(batch);
        await context.SaveChangesAsync(cancellationToken);

        if (tombstones)
        {
            foreach (var stored in batch)
                await sync.PlaceTombstoneAsync(calendarId, stored.DavName, rank, cancellationToken);
        }

        return batch.Count;
    }
}
