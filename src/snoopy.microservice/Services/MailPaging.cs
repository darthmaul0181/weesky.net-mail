using MailKit;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The pure paging and ordering arithmetic behind message listing and search: page windows,
/// candidate bounds, budget splits and merge ordering. No IMAP call anywhere — which is what
/// keeps every rule here unit-testable apart from a server.
/// </summary>
internal static class MailPaging
{
    /// <summary>
    /// How many BODYSTRUCTUREs one attachment-filtered search may examine past the page window.
    /// No IMAP SEARCH criterion covers attachments, so the filter must look at candidates; at a
    /// few hundred bytes of summary each, two thousand costs a few hundred KB and well under a
    /// second, so a realistic search is examined whole and its count stays exact — only the
    /// whole-mailbox sweep is cut, and its Total degrades to "at least what was examined".
    /// </summary>
    internal const int AttachmentScanBudget = 2000;

    /// <summary>A merge-path match: folder+uid to fetch, the sent-date sort key, its attachment flag.</summary>
    internal sealed record SearchHit(UniqueId Uid, IMailFolder Folder, DateTimeOffset SortKey, bool HasAttachment);

    /// <summary>
    /// The per-folder candidate window: once every folder's newest-first list is merged, only its
    /// first (page + 1) * pageSize entries can still land on the requested page, so nothing past
    /// them is worth a per-message FETCH. <paramref name="floor"/> widens the window when a
    /// post-filter needs to look deeper than the page alone (the attachment scan budget).
    /// Long arithmetic: the controller caps pageSize, not page.
    /// </summary>
    internal static IList<UniqueId> BoundCandidates(IList<UniqueId> newestFirst, int page, int pageSize, long floor = 0)
    {
        var take = Math.Max((page + 1L) * pageSize, floor);
        if (take <= 0) return [];

        return take >= newestFirst.Count ? newestFirst : newestFirst.Take((int)take).ToList();
    }

    /// <summary>How many date-ordered hits the non-SORT attachment filter may examine.</summary>
    internal static int ExamineLimit(int page, int pageSize) =>
        (int)Math.Min(int.MaxValue, Math.Max((page + 1L) * pageSize, AttachmentScanBudget));

    /// <summary>
    /// Splits the attachment scan budget across folders by need, never by list position: a
    /// folder wanting less than an equal split keeps only what it has, and what it leaves is
    /// re-split among the larger ones — so which folders scan deep cannot depend on the
    /// alphabetical order the folder list happens to arrive in.
    /// </summary>
    internal static IReadOnlyList<long> FairShares(IReadOnlyList<int> counts, long budget)
    {
        var shares = new long[counts.Count];
        var bySize = Enumerable.Range(0, counts.Count).OrderBy(index => counts[index]).ToList();
        var remaining = budget;
        for (var n = 0; n < bySize.Count; n++)
        {
            var index = bySize[n];
            var share = Math.Min(counts[index], remaining / (bySize.Count - n));
            shares[index] = share;
            remaining -= share;
        }

        return shares;
    }

    /// <summary>
    /// The merge sort key: the sent date, falling back to arrival then MinValue so a malformed
    /// message missing its Envelope.Date still orders sanely and never yields null.
    /// </summary>
    internal static DateTimeOffset SortKeyOf(DateTimeOffset? sentDate, DateTimeOffset? internalDate)
        => sentDate ?? internalDate ?? DateTimeOffset.MinValue;

    /// <summary>
    /// Orders merge-path hits by sent date, newest first, optionally keeping only those carrying an
    /// attachment. Pure so the refine-before-Total ordering is unit-tested apart from any IMAP call.
    /// </summary>
    internal static List<SearchHit> OrderHits(IEnumerable<SearchHit> hits, bool attachmentsOnly)
    {
        var kept = attachmentsOnly ? hits.Where(hit => hit.HasAttachment) : hits;
        return kept.OrderByDescending(hit => hit.SortKey).ToList();
    }

    /// <summary>
    /// Maps a newest-first page onto an IMAP sequence range, which runs oldest-first: page
    /// zero is the window at the *end* of the folder. Returns (-1, -1) when the page lies
    /// past the end, or when the arguments make no sense.
    /// </summary>
    public static (int Start, int End) ComputePageWindow(int total, int page, int pageSize)
    {
        if (total <= 0 || page < 0 || pageSize <= 0) return (-1, -1);

        var end = total - 1 - (page * pageSize);
        if (end < 0) return (-1, -1);

        var start = Math.Max(0, end - pageSize + 1);
        return (start, end);
    }

    /// <summary>Slice of an already-ordered list, or empty when the page lies past its end.</summary>
    public static IReadOnlyList<T> PageOf<T>(IEnumerable<T> ordered, int page, int pageSize)
    {
        if (page < 0 || pageSize <= 0) return [];

        // Long arithmetic, clamped: an overflowing page must read "past the end", never page 0.
        var skip = (int)Math.Min((long)page * pageSize, int.MaxValue);
        return ordered.Skip(skip).Take(pageSize).ToList();
    }

    /// <summary>
    /// Re-orders fetched items to match the order they were asked for. A server answers a UID
    /// FETCH in whatever order it likes — ascending UID in practice, which is exactly not the
    /// sort order requested.
    /// </summary>
    public static IReadOnlyList<T> InOrderOf<T, TKey>(
        IEnumerable<T> items, IEnumerable<TKey> order, Func<T, TKey> keyOf) where TKey : notnull
    {
        var byKey = items.GroupBy(keyOf).ToDictionary(group => group.Key, group => group.First());

        return order.Where(byKey.ContainsKey).Select(key => byKey[key]).ToList();
    }
}
