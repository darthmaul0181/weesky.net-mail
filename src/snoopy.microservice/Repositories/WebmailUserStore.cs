using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class WebmailUserStore(PreferencesDbContext context) : IWebmailUserStore
{
    public async Task<Guid> RegisterLoginAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var now = DateTime.UtcNow;

        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);
        if (existing is not null)
        {
            existing.LastLoginDate = now;
            await context.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var row = new WebmailUser { Id = Guid.NewGuid(), Email = canonical, CreationDate = now, LastLoginDate = now };
        context.Users.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return row.Id;
        }
        catch (DbUpdateException)
        {
            // A concurrent first login inserted the same email; adopt the winner's row.
            context.Entry(row).State = EntityState.Detached;
            var winner = await context.Users
                .FirstAsync(u => u.Email == canonical, cancellationToken);
            winner.LastLoginDate = now;
            await context.SaveChangesAsync(cancellationToken);
            return winner.Id;
        }
    }

    public async Task DeleteByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);
        if (existing is null) return;

        context.Users.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string Canonical(string email) => email.Trim().ToLowerInvariant();
}
