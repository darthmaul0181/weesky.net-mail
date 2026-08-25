using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SyncStateConsistencyCheckTests
{
    private static PreferencesTestDbContext NewContextWith(ulong seq, ulong highestContactRank)
    {
        var context = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();

        context.ContactSyncStates.Add(new ContactSyncState
        {
            UserId = userId, Epoch = Guid.NewGuid(), Seq = seq, PrunedBelow = 0
        });
        context.Contacts.Add(NewContact(userId, highestContactRank));
        context.SaveChanges();

        return context;
    }

    private static PreferencesTestDbContext NewContextWithContactsOnly(ulong highestContactRank)
    {
        var context = new PreferencesTestDbContext(Guid.NewGuid().ToString());
        context.Contacts.Add(NewContact(Guid.NewGuid(), highestContactRank));
        context.SaveChanges();

        return context;
    }

    private static Contact NewContact(Guid userId, ulong syncSequence) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Uid = Guid.NewGuid().ToString(), SyncSequence = syncSequence
    };

    [Fact]
    public async Task ABookInStep_SaysNothing()
    {
        using var context = NewContextWith(seq: 10, highestContactRank: 10);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        logger.VerifyNoErrorLogged();
    }

    [Fact]
    public async Task AContactAheadOfItsState_IsLoggedAsAnError()
    {
        using var context = NewContextWith(seq: 3, highestContactRank: 11);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        // A contact cannot outrank its own counter unless the two tables came from different
        // snapshots. Named, with the .sql line to run beside it — an operator reading this line at
        // three in the morning must not have to find the remedy in a design document.
        logger.VerifyErrorLoggedContaining("contacts-sync-epoch-rotate.sql");
    }

    [Fact]
    public async Task AConsistentRestore_IsInvisibleToIt_AndThatIsWhyTheNoteExists()
    {
        // Both tables rewound together: MAX(sync_sequence) <= seq still holds, so this check is
        // silent while every client's token now covers ranks whose content changed. Recorded as a
        // test so nobody comes to rely on the check for the case it cannot see.
        using var context = NewContextWith(seq: 5, highestContactRank: 5);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        logger.VerifyNoErrorLogged();
    }

    [Fact]
    public async Task AUserWithNoStateRow_IsNotAnError()
    {
        // Every account created after the deployment is in this shape until its first write.
        using var context = NewContextWithContactsOnly(highestContactRank: 0);
        var logger = new Mock<ILogger<SyncStateConsistencyCheck>>();
        var check = new SyncStateConsistencyCheck(context, logger.Object);

        await check.RunAsync(CancellationToken.None);

        logger.VerifyNoErrorLogged();
    }
}
