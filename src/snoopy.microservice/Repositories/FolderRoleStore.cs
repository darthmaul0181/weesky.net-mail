using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class FolderRoleStore(PreferencesDbContext context) : IFolderRoleStore
{
    public async Task<IReadOnlyList<FolderRoleOverride>> GetAsync(
        Guid userId, string accountId, CancellationToken cancellationToken)
        => await context.FolderRoleOverrides.AsNoTracking()
            .Where(o => o.UserId == userId && o.AccountId == accountId)
            .OrderBy(o => o.Role)
            .ToListAsync(cancellationToken);

    public async Task UpsertAsync(FolderRoleOverride @override, CancellationToken cancellationToken)
    {
        var existing = await context.FolderRoleOverrides.FirstOrDefaultAsync(
            o => o.UserId == @override.UserId && o.AccountId == @override.AccountId
                 && o.Role == @override.Role, cancellationToken);

        if (existing == null)
        {
            @override.UpdatedAt = DateTime.UtcNow;
            context.FolderRoleOverrides.Add(@override);
        }
        else
        {
            existing.FolderPath = @override.FolderPath;
            existing.UidValidity = @override.UidValidity;
            existing.MailboxId = @override.MailboxId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid userId, string accountId, string role, CancellationToken cancellationToken)
    {
        var existing = await context.FolderRoleOverrides.FirstOrDefaultAsync(
            o => o.UserId == userId && o.AccountId == accountId && o.Role == role, cancellationToken);
        if (existing == null) return;

        context.FolderRoleOverrides.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyRenameAsync(Guid userId, string accountId, string oldPath, string newPath,
        char separator, ulong newUidValidity, string? newMailboxId, CancellationToken cancellationToken)
    {
        var prefix = oldPath + separator;
        var rows = await context.FolderRoleOverrides
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
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSubtreeAsync(Guid userId, string accountId, string path, char separator,
        CancellationToken cancellationToken)
    {
        var prefix = path + separator;
        var rows = await context.FolderRoleOverrides
            .Where(o => o.UserId == userId && o.AccountId == accountId
                        && (o.FolderPath == path || o.FolderPath.StartsWith(prefix)))
            .ToListAsync(cancellationToken);

        context.FolderRoleOverrides.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
    }
}
