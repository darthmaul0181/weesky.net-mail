using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

internal sealed class SchedulingAccountStore(PreferencesDbContext context)
    : ScopedStore<SchedulingServiceAccount>(context), ISchedulingAccountStore
{
    public Task<SchedulingServiceAccount?> FindAsync(CancellationToken cancellationToken)
        => Untracked.FirstOrDefaultAsync(a => a.Id == SchedulingServiceAccount.SingletonId, cancellationToken);

    public Task<bool> SaveAsync(string host, int port, string security, string login, byte[] passwordCipher,
        CancellationToken cancellationToken)
        => WriteAsync(host, port, security, login, passwordCipher, checkedVersion: null, cancellationToken);

    public Task<bool> SaveKeepingPasswordAsync(string host, int port, string security, string login, DateTime checkedVersion,
        CancellationToken cancellationToken)
        => WriteAsync(host, port, security, login, passwordCipher: null, checkedVersion, cancellationToken);

    public Task<bool> DeleteAsync(CancellationToken cancellationToken)
        => LastWriterWinsAsync(async () =>
        {
            await RemoveWhereAsync(Set, a => a.Id == SchedulingServiceAccount.SingletonId, cancellationToken);
            await Context.SaveChangesAsync(cancellationToken);
            return true;
        });

    public async Task<bool> RecordTestAsync(DateTime testedAt, bool ok, DateTime testedVersion,
        CancellationToken cancellationToken)
    {
        var row = await Set.FirstOrDefaultAsync(a => a.Id == SchedulingServiceAccount.SingletonId, cancellationToken);
        if (row is null || row.UpdatedAt != testedVersion) return false;

        row.LastTestAt = testedAt;
        row.LastTestOk = ok;
        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // updated_at is the concurrency token: a save committed after the check above.
            return false;
        }
    }

    /// <summary>A kept password stays only on the row the caller checked it against: every attempt re-verifies
    /// that version, so another admin's password is never paired with this save's host.</summary>
    private Task<bool> WriteAsync(string host, int port, string security, string login, byte[]? passwordCipher,
        DateTime? checkedVersion, CancellationToken cancellationToken)
        => LastWriterWinsAsync(async () =>
        {
            var row = await Set.FirstOrDefaultAsync(a => a.Id == SchedulingServiceAccount.SingletonId, cancellationToken);
            if (passwordCipher is null && row?.UpdatedAt != checkedVersion) return false;
            if (row is null) Set.Add(row = new SchedulingServiceAccount());

            Write(row, DateTime.UtcNow, host, port, security, login, passwordCipher ?? row.PasswordCipher);
            await Context.SaveChangesAsync(cancellationToken);
            return true;
        });

    /// <summary>
    /// Last writer wins, by commit order: a write that lost a race to another save or delete reads the
    /// row again and applies once more. False when it loses twice, or when <paramref name="write"/> says so.
    /// </summary>
    private async Task<bool> LastWriterWinsAsync(Func<Task<bool>> write)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await write();
            }
            catch (DbUpdateException ex) when (LostRace(ex))
            {
                Context.ChangeTracker.Clear();
                if (attempt == 2) return false;
            }
        }
    }

    /// <summary>updated_at moved or the row vanished; or two first saves both inserted id 1.</summary>
    private static bool LostRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException || ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };

    private static SchedulingServiceAccount Write(SchedulingServiceAccount row, DateTime now,
        string host, int port, string security, string login, byte[] passwordCipher)
    {
        row.Host = host;
        row.Port = port;
        row.Security = security;
        row.Login = login;
        row.PasswordCipher = passwordCipher;
        row.LastTestAt = null;
        row.LastTestOk = null;
        row.UpdatedAt = now;
        return row;
    }
}
