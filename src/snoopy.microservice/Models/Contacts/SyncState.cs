namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// The three numbers every token and ctag is cut from, read together. The watermark and the
/// tombstones must be read in the same transaction — the same InnoDB snapshot: a prune slipping in
/// between would make the response miss deletions under a watermark already stale, and reopen by a
/// race the very hole the column closes.
/// </summary>
/// <param name="Epoch">Rotated only by a restore; it makes every token the old database issued foreign.</param>
/// <param name="Seq">The rank of the most recent write.</param>
/// <param name="PrunedBelow">A token at or below this is unrecoverable.</param>
public sealed record SyncState(Guid Epoch, ulong Seq, ulong PrunedBelow);
