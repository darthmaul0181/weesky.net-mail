using MailKit;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The pure arithmetic behind conversation grouping: flattens the THREAD tree into per-thread
/// UID sets and orders both members and threads off the SORT result. No IMAP call anywhere —
/// the MailPaging pattern, so every rule is unit-testable apart from a server.
/// </summary>
internal static class MailThreading
{
    /// <summary>
    /// Each thread's UIDs newest-first, threads ordered by their newest member (newest first).
    /// A uid the sort does not know is dropped — THREAD and SORT are two commands, and a
    /// message expunged between them must not surface a row the fetch cannot fill.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<UniqueId>> Arrange(
        IList<MessageThread> tree, IList<UniqueId> newestFirst)
    {
        var rank = new Dictionary<UniqueId, int>(newestFirst.Count);
        for (var index = 0; index < newestFirst.Count; index++) rank[newestFirst[index]] = index;

        var threads = new List<List<UniqueId>>();
        foreach (var root in tree)
        {
            var members = new List<UniqueId>();
            Collect(root, members);

            var known = members.Where(rank.ContainsKey).OrderBy(uid => rank[uid]).ToList();
            if (known.Count > 0) threads.Add(known);
        }

        // A thread sits where its newest member sits.
        return threads.OrderBy(thread => rank[thread[0]]).ToList();
    }

    private static void Collect(MessageThread node, List<UniqueId> members)
    {
        if (node.UniqueId is { } uid) members.Add(uid);
        foreach (var child in node.Children) Collect(child, members);
    }
}
