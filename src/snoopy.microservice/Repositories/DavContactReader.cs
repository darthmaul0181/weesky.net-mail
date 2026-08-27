using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <inheritdoc cref="IDavContactReader"/>
internal sealed class DavContactReader(PreferencesDbContext context) : IDavContactReader
{
    private static readonly Expression<Func<Contact, DavCard>> ToCard = c =>
        new DavCard(c.Id, c.DavName!, c.Uid, c.VCardRaw!, c.CardHash, c.UpdatedAt, c.SyncSequence);

    public IAsyncEnumerable<DavCard> StreamAsync(Guid userId, CancellationToken cancellationToken) =>
        Visible(userId).Select(ToCard).AsAsyncEnumerable();

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

    /// The three-condition visibility clause, written once so none of the four queries above can
    /// forget one and serve a card the 4a backfill has not finished reaching with an empty ETag.
    private IQueryable<Contact> Visible(Guid userId) =>
        context.Contacts.Where(c =>
            c.UserId == userId && c.DavName != null && c.VCardRaw != null && c.CardHash != "");
}
