using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

internal sealed class PreferencesTestDbContext : PreferencesDbContext
{
    // The InMemory provider keys its named databases by internal service provider, not by name
    // alone: two DbContexts built from differently-configured options (here, ConfigureWarnings
    // below versus a caller building its own PreferencesDbContext options directly) would
    // otherwise land on two separate, mutually invisible stores despite sharing a database name.
    // A shared root is the documented way to keep them one store regardless of that mismatch.
    internal static readonly InMemoryDatabaseRoot Root = new();

    // The InMemory provider has no transactions: BeginTransactionAsync throws by default rather
    // than silently no-op, to warn a caller relying on one. The transaction ContactStore opens is
    // real in production and ignored here — one more reason the sequence counter's atomicity
    // (IContactSyncStore.NextSequenceAsync) is verified by hand rather than by a test on InMemory.
    //
    // Ignoring it also makes the transaction INVISIBLE: CurrentTransaction stays null even after
    // BeginTransactionAsync, so nothing can witness that a caller opened one. keepTransactionsFatal
    // restores the default refusal, which is the only observable a test has for "a snapshot was
    // opened here" — the isolation itself remains beyond this provider either way.
    public PreferencesTestDbContext(string databaseName, bool keepTransactionsFatal = false)
        : base(new DbContextOptionsBuilder<PreferencesDbContext>()
              .UseInMemoryDatabase(databaseName, Root)
              .ConfigureWarnings(w =>
              {
                  if (!keepTransactionsFatal) w.Ignore(InMemoryEventId.TransactionIgnoredWarning);
              })
              .Options)
    {
    }
}
