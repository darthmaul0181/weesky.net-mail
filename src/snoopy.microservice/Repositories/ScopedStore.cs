using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The three shapes the webmail preference stores share over <see cref="PreferencesDbContext"/>:
/// a never-tracked scoped read, a keyed find-or-create stamped from one clock reading, and a
/// scoped delete that leaves the commit to its caller. A store that does not fit stays out.
/// </summary>
internal abstract class ScopedStore<TEntity>(PreferencesDbContext context)
    where TEntity : class
{
    protected PreferencesDbContext Context { get; } = context;

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    /// <summary>
    /// Reads are never tracked. A caller that later <c>Attach</c>es a row it read back depends on
    /// that, and nothing outside this type enforces it.
    /// </summary>
    protected IQueryable<TEntity> Untracked => Set.AsNoTracking();

    protected IQueryable<TEntity> Scoped(Expression<Func<TEntity, bool>> scope)
        => Untracked.Where(scope);

    /// <summary>
    /// One clock reading feeds both branches, so a row lands with the same stamp whether it was
    /// created or updated.
    /// </summary>
    protected async Task UpsertByKeyAsync(
        Expression<Func<TEntity, bool>> key,
        Func<DateTime, TEntity> create,
        Action<TEntity, DateTime> update,
        CancellationToken cancellationToken)
    {
        var existing = await Set.FirstOrDefaultAsync(key, cancellationToken);
        var now = DateTime.UtcNow;

        if (existing is null) Set.Add(create(now));
        else update(existing, now);

        await Context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Loaded and removed rather than bulk-deleted, and left unsaved: the caller decides what
    /// commits alongside it, and the InMemory provider the tests run on translates no SQL.
    /// </summary>
    protected static async Task RemoveWhereAsync<T>(
        DbSet<T> set, Expression<Func<T, bool>> scope, CancellationToken cancellationToken)
        where T : class
        => set.RemoveRange(await set.Where(scope).ToListAsync(cancellationToken));
}
