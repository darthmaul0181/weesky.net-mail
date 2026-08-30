using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactTombstoneSweeperTests
{
    // Tests default the startup jitter to zero: they assert the sweep runs promptly, not that it
    // waits out the real staggering delay production uses.
    private static ContactTombstoneSweeper NewSweeper(
        IContactSyncStore store, ILogger<ContactTombstoneSweeper>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => store);

        return new ContactTombstoneSweeper(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<ContactTombstoneSweeper>.Instance,
            TimeSpan.Zero);
    }

    private static bool WithinAnHourOf(DateTime actual, DateTime expected) =>
        Math.Abs((actual - expected).TotalHours) < 1;

    [Fact]
    public async Task OnePass_PrunesTombstonesAtOneHundredAndEightyDays()
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.PruneAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PruneOutcome(2, 1));
        var sweeper = NewSweeper(sync.Object);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        sync.Verify(s => s.PruneAsync(
            It.Is<DateTime>(d => WithinAnHourOf(d, DateTime.UtcNow.AddDays(-180))),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnePass_PrunesRevisionsAtThirtyDays()
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.PruneAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PruneOutcome(0, 0));
        var sweeper = NewSweeper(sync.Object);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        // Thirty and not a hundred and eighty: the tombstone is what the protocol must still be
        // able to tell a client gone a long time, the revision is what a human might still want
        // back. Past thirty days a deleted card stays correctly deleted everywhere — it is simply
        // no longer restorable.
        sync.Verify(s => s.PruneAsync(
            It.IsAny<DateTime>(),
            It.Is<DateTime>(d => WithinAnHourOf(d, DateTime.UtcNow.AddDays(-30))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnePass_LogsItsOutcomeEvenWhenItRemovedNothing()
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.PruneAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PruneOutcome(0, 0));
        var logger = new Mock<ILogger<ContactTombstoneSweeper>>();
        var sweeper = NewSweeper(sync.Object, logger.Object);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        // Zero included, so the line doubles as the sweeper's heartbeat — the convention the two
        // existing sweepers already follow.
        logger.VerifyInformationLogged();
        // And nothing alarming on an ordinary pass, or the warning below stops meaning anything.
        logger.VerifyNoWarningLogged();
    }

    [Fact]
    public async Task OnePass_ThatHitItsCeiling_SaysSoOutLoud()
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.PruneAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PruneOutcome(ContactSyncStore.MaxRowsPerSweep, 0, Capped: true));
        var logger = new Mock<ILogger<ContactTombstoneSweeper>>();
        var sweeper = NewSweeper(sync.Object, logger.Object);

        await sweeper.SweepOnceAsync(CancellationToken.None);

        // The heartbeat line reads as "everything old is gone" whatever the numbers on it, so a
        // bounded pass needs a line of its own: several of these in a row is a sweeper that has not
        // been running, which is exactly what nobody notices from a count.
        logger.VerifyWarningLoggedContaining("ceiling");
    }
}
