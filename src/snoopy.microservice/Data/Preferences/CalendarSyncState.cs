using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One row per calendar, holding the counter every sync token and ctag is cut from. Per calendar
/// and not per user, unlike <see cref="ContactSyncState"/>: CalDAV syncs each collection on its own.
/// </summary>
[Table("calendar_sync_state")]
public sealed class CalendarSyncState
{
    [Column("calendar_id")]
    public Guid CalendarId { get; set; }

    /// <summary>
    /// Drawn once and never moved in normal operation. A restore rewinds <see cref="Seq"/>, and
    /// rotating this is what makes every token the old database issued foreign to the collection.
    /// </summary>
    [Column("epoch")]
    public Guid Epoch { get; set; }

    /// <summary>
    /// Named <c>seq</c> because SEQUENCE is a MariaDB keyword since 10.3, and a column that only
    /// exists between back-quotes is a production error waiting in a project where SQL is run by
    /// hand.
    /// </summary>
    [Column("seq")]
    public ulong Seq { get; set; }

    /// <summary>
    /// The highest pruned rank. A token strictly below it is unrecoverable — a tombstone it would
    /// need is gone — and answers 403 valid-sync-token; a token AT it still resolves.
    /// </summary>
    [Column("pruned_below")]
    public ulong PrunedBelow { get; set; }
}
