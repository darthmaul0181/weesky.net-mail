using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Compares, per user, <c>MAX(contacts.sync_sequence)</c> against <c>contact_sync_state.seq</c>. A
/// contact cannot legitimately outrank its own counter — the two can only disagree that way when
/// they came from different snapshots, e.g. a restore that replaced one table but not the other.
///
/// This catches only half of what a bad restore can do: a *consistent* restore, both tables
/// rewound together, leaves the inequality true and this check silent, while every client's token
/// now covers ranks whose content actually changed underneath it. The remedy either way is the
/// same file, <c>assets/contacts-sync-epoch-rotate.sql</c> — see
/// <c>docs/superpowers/carddav-restore-prerequisite.md</c>.
/// </summary>
internal sealed class SyncStateConsistencyCheck(
    PreferencesDbContext context, ILogger<SyncStateConsistencyCheck> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
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
                    "assets/contacts-sync-epoch-rotate.sql for this user. This check cannot see a " +
                    "consistent restore: both tables rewound together leave MAX(sync_sequence) <= seq " +
                    "true, so it stays silent while every client's token now covers ranks whose content " +
                    "changed.",
                    row.UserId, row.HighestContactRank, seq);
            }
        }
    }
}
