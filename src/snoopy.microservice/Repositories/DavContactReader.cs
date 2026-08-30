using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IDavContactReader"/>
internal sealed class DavContactReader(PreferencesDbContext context) : IDavContactReader
{
    private static readonly Expression<Func<Contact, DavCard>> ToCard = c =>
        new DavCard(c.Id, c.DavName!, c.Uid, c.VCardRaw!, c.CardHash, c.UpdatedAt, c.SyncSequence);

    public async IAsyncEnumerable<DavCard> StreamAsync(
        Guid userId, ulong upTo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cards = Visible(userId)
            .Where(c => c.SyncSequence <= upTo)
            .Select(ToCard)
            .AsAsyncEnumerable();
        await foreach (var card in cards.WithCancellation(cancellationToken))
        {
            yield return card;
        }
    }

    public async Task<DavCard?> FindAsync(Guid userId, string davName, CancellationToken cancellationToken) =>
        await Visible(userId)
            .Where(c => c.DavName == davName)
            .Select(ToCard)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DavCard>> FindManyAsync(
        Guid userId, IReadOnlyList<string> davNames, CancellationToken cancellationToken) =>
        await Visible(userId)
            .Where(c => davNames.Contains(c.DavName))
            .Select(ToCard)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(Guid userId, CancellationToken cancellationToken) =>
        await Visible(userId).CountAsync(cancellationToken);

    public async IAsyncEnumerable<DavCard> ChangedAsync(Guid userId, ulong after, ulong upTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Ordered by rank, both bounds inclusive-exclusive as documented: the order is what makes
        // a truncation able to cut on a rank boundary, and no small-volume test would catch losing it.
        var cards = Visible(userId)
            .Where(c => c.SyncSequence > after && c.SyncSequence <= upTo)
            .OrderBy(c => c.SyncSequence)
            .Select(ToCard)
            .AsAsyncEnumerable();
        await foreach (var card in cards.WithCancellation(cancellationToken))
        {
            yield return card;
        }
    }

    public async Task<IReadOnlyList<ContactTombstone>> TombstonesAsync(
        Guid userId, ulong after, ulong upTo, CancellationToken cancellationToken) =>
        await context.ContactTombstones
            .Where(t => t.UserId == userId && t.SyncSequence > after && t.SyncSequence <= upTo)
            .OrderBy(t => t.SyncSequence)
            .ToListAsync(cancellationToken);

    /// The three-condition visibility clause, written once so none of the four queries above can
    /// forget one and serve a card the 4a backfill has not finished reaching with an empty ETag.
    private IQueryable<Contact> Visible(Guid userId) =>
        context.Contacts.Where(c =>
            c.UserId == userId && c.DavName != null && c.VCardRaw != null && c.CardHash != "");
}
