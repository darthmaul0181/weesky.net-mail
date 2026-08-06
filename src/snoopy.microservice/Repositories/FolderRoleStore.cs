using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class FolderRoleStore(PreferencesDbContext context)
    : ScopedStore<FolderRoleOverride>(context), IFolderRoleStore
{
    public async Task<IReadOnlyList<FolderRoleOverride>> GetAsync(
        Guid userId, string accountId, CancellationToken cancellationToken)
        => await Scoped(o => o.UserId == userId && o.AccountId == accountId)
            .OrderBy(o => o.Role)
            .ToListAsync(cancellationToken);

    public Task UpsertAsync(FolderRoleOverride @override, CancellationToken cancellationToken)
        => UpsertByKeyAsync(
            o => o.UserId == @override.UserId && o.AccountId == @override.AccountId
                 && o.Role == @override.Role,
            now =>
            {
                @override.UpdatedAt = now;
                return @override;
            },
            (existing, now) =>
            {
                existing.FolderPath = @override.FolderPath;
                existing.UidValidity = @override.UidValidity;
                existing.MailboxId = @override.MailboxId;
                existing.UpdatedAt = now;
            },
            cancellationToken);

    public async Task DeleteAsync(
        Guid userId, string accountId, string role, CancellationToken cancellationToken)
    {
        var existing = await Set.FirstOrDefaultAsync(
            o => o.UserId == userId && o.AccountId == accountId && o.Role == role, cancellationToken);
        if (existing == null) return;

        Set.Remove(existing);
        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyRenameAsync(Guid userId, string accountId, string oldPath, string newPath,
        char separator, ulong newUidValidity, string? newMailboxId, CancellationToken cancellationToken)
    {
        var prefix = oldPath + separator;
        var rows = await Set
            .Where(o => o.UserId == userId && o.AccountId == accountId
                        && (o.FolderPath == oldPath || o.FolderPath.StartsWith(prefix)))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (row.FolderPath == oldPath)
            {
                row.FolderPath = newPath;
                row.UidValidity = newUidValidity;
                row.MailboxId = newMailboxId;
            }
            else
            {
                row.FolderPath = newPath + row.FolderPath.Substring(oldPath.Length);
            }
            row.UpdatedAt = DateTime.UtcNow;
        }

        // A single SaveChanges: on a relational provider this commits as one transaction.
        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSubtreeAsync(Guid userId, string accountId, string path, char separator,
        CancellationToken cancellationToken)
    {
        var prefix = path + separator;
        await RemoveWhereAsync(Set, o => o.UserId == userId && o.AccountId == accountId
                                         && (o.FolderPath == path || o.FolderPath.StartsWith(prefix)),
            cancellationToken);

        await Context.SaveChangesAsync(cancellationToken);
    }
}
