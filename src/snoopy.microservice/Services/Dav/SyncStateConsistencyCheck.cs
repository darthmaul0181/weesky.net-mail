using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// Compares, per user, <c>MAX(contacts.sync_sequence)</c> against <c>contact_sync_state.seq</c>,
/// then, per calendar, <c>MAX(calendar_events.sync_sequence)</c> against
/// <c>calendar_sync_state.seq</c>. A row cannot legitimately outrank its own counter — the two can
/// only disagree that way when they came from different snapshots, e.g. a restore that replaced one
/// table but not the other. No longer a check of contacts alone (5c): comparing two counters is
/// WebDAV's own concern, not the address book's, which is why this moved from
/// <c>Services/CardDav</c> to here.
///
/// This catches only half of what a bad restore can do: a *consistent* restore, both tables
/// rewound together, leaves the inequality true and this check silent, while every client's token
/// now covers ranks whose content actually changed underneath it. The remedy either way is the
/// same file — <c>assets/contacts-sync-epoch-rotate.sql</c> for a book, <c>assets/calendar-sync-epoch-rotate.sql</c>
/// for a calendar — but not the same statement in it: what this check finds is one user's book or
/// one calendar, so it takes the single-row form, while a restore is the whole database's and takes
/// the whole-database one, which costs every synchronising device in the deployment a manual
/// re-pairing. See <c>docs/superpowers/carddav-restore-prerequisite.md</c>.
/// </summary>
internal sealed class SyncStateConsistencyCheck(
    PreferencesDbContext context, ILogger<SyncStateConsistencyCheck> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await CheckContactsAsync(cancellationToken);
        await CheckCalendarsAsync(cancellationToken);
    }

    private async Task CheckContactsAsync(CancellationToken cancellationToken)
    {
        var highestByUser = await context.Contacts
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, HighestContactRank = g.Max(c => c.SyncSequence) })
            .ToListAsync(cancellationToken);

        var seqByUser = await context.ContactSyncStates
            .ToDictionaryAsync(s => s.UserId, s => s.Seq, cancellationToken);

        foreach (var row in highestByUser)
        {
            // No state row means no token was ever issued for this user — every account created
            // after the deployment is in this shape until its first write, and there is nothing to
            // compare against.
            if (!seqByUser.TryGetValue(row.UserId, out var seq)) continue;

            if (row.HighestContactRank > seq)
            {
                logger.LogError(
                    "Sync state inconsistency for user {UserId}: contacts.sync_sequence reaches " +
                    "{HighestContactRank} but contact_sync_state.seq is only {Seq}. A contact cannot " +
                    "outrank its own counter unless the two tables came from different snapshots — run " +
                    "the single-user form of assets/contacts-sync-epoch-rotate.sql, the commented " +
                    "UPDATE ... WHERE user_id = ... at its foot, on this user_id alone. Not the " +
                    "whole-database statement above it: that one is the remedy for a full restore, and " +
                    "it forces every Thunderbird address book in the deployment to be deleted and " +
                    "re-created by hand. This check cannot see a " +
                    "consistent restore: both tables rewound together leave MAX(sync_sequence) <= seq " +
                    "true, so it stays silent while every client's token now covers ranks whose content " +
                    "changed.",
                    row.UserId, row.HighestContactRank, seq);
            }
        }
    }

    private async Task CheckCalendarsAsync(CancellationToken cancellationToken)
    {
        var highestByCalendar = await context.CalendarEvents
            .GroupBy(e => e.CalendarId)
            .Select(g => new { CalendarId = g.Key, HighestEventRank = g.Max(e => e.SyncSequence) })
            .ToListAsync(cancellationToken);

        var seqByCalendar = await context.CalendarSyncStates
            .ToDictionaryAsync(s => s.CalendarId, s => s.Seq, cancellationToken);

        foreach (var row in highestByCalendar)
        {
            // No state row means no token was ever issued for this calendar — every calendar
            // created after the deployment is in this shape until its first write.
            if (!seqByCalendar.TryGetValue(row.CalendarId, out var seq)) continue;

            if (row.HighestEventRank > seq)
            {
                logger.LogError(
                    "Sync state inconsistency for calendar {CalendarId}: " +
                    "calendar_events.sync_sequence reaches {HighestEventRank} but " +
                    "calendar_sync_state.seq is only {Seq}. An event cannot outrank its own counter " +
                    "unless the two tables came from different snapshots — run the single-calendar " +
                    "form of assets/calendar-sync-epoch-rotate.sql, the commented UPDATE ... WHERE " +
                    "calendar_id = ... at its foot, on this calendar_id alone. Not the whole-database " +
                    "statement above it: that one is the remedy for a full restore, and it forces " +
                    "every synchronising device in the deployment to be re-paired by hand. This check " +
                    "cannot see a consistent restore: both tables rewound together leave " +
                    "MAX(sync_sequence) <= seq true, so it stays silent while every client's token now " +
                    "covers ranks whose content changed.",
                    row.CalendarId, row.HighestEventRank, seq);
            }
        }
    }
}
