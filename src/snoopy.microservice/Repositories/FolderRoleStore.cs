using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class FolderRoleStore : IFolderRoleStore
{
    private readonly PreferencesDbContext _context;

    public FolderRoleStore(PreferencesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<FolderRoleOverride>> GetAsync(Guid userId, CancellationToken cancellationToken)
        => await _context.FolderRoleOverrides.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderBy(o => o.Role)
            .ToListAsync(cancellationToken);

    public async Task UpsertAsync(FolderRoleOverride @override, CancellationToken cancellationToken)
    {
        var existing = await _context.FolderRoleOverrides.FirstOrDefaultAsync(
            o => o.UserId == @override.UserId && o.Role == @override.Role, cancellationToken);

        if (existing == null)
        {
            @override.UpdatedAt = DateTime.UtcNow;
            _context.FolderRoleOverrides.Add(@override);
        }
        else
        {
            existing.FolderPath = @override.FolderPath;
            existing.UidValidity = @override.UidValidity;
            existing.MailboxId = @override.MailboxId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        var existing = await _context.FolderRoleOverrides.FirstOrDefaultAsync(
            o => o.UserId == userId && o.Role == role, cancellationToken);
        if (existing == null) return;

        _context.FolderRoleOverrides.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyRenameAsync(Guid userId, string oldPath, string newPath, char separator,
        ulong newUidValidity, string? newMailboxId, CancellationToken cancellationToken)
    {
        var prefix = oldPath + separator;
        var rows = await _context.FolderRoleOverrides
            .Where(o => o.UserId == userId
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
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSubtreeAsync(Guid userId, string path, char separator, CancellationToken cancellationToken)
    {
        var prefix = path + separator;
        var rows = await _context.FolderRoleOverrides
            .Where(o => o.UserId == userId
                        && (o.FolderPath == path || o.FolderPath.StartsWith(prefix)))
            .ToListAsync(cancellationToken);

        _context.FolderRoleOverrides.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
