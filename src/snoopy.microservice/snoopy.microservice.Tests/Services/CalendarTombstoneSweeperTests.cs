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

public sealed class CalendarTombstoneSweeperTests
{
    // Tests default the startup jitter to zero: they assert the sweep runs promptly, not that it
    // waits out the real staggering delay production uses.
    private static CalendarTombstoneSweeper NewSweeper(
        ICalendarSyncStore store, ILogger<CalendarTombstoneSweeper>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => store);

        return new CalendarTombstoneSweeper(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<CalendarTombstoneSweeper>.Instance,
            TimeSpan.Zero);
    }

    private static Mock<ICalendarSyncStore> Pruning(PruneOutcome outcome)
    {
        var sync = new Mock<ICalendarSyncStore>();
        sync.Setup(s => s.PruneAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        return sync;
    }

    private static bool WithinAnHourOf(DateTime actual, DateTime expected) =>
        Math.Abs((actual - expected).TotalHours) < 1;

    [Fact]
    public async Task OnePass_PrunesTombstonesAtOneHundredAndEightyDays()
    {
        var sync = Pruning(new PruneOutcome(2, 1));

        await NewSweeper(sync.Object).SweepOnceAsync(CancellationToken.None);

        sync.Verify(s => s.PruneAsync(
            It.Is<DateTime>(d => WithinAnHourOf(d, DateTime.UtcNow.AddDays(-180))),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnePass_PrunesRevisionsAtThirtyDays()
    {
        var sync = Pruning(new PruneOutcome(0, 0));

        await NewSweeper(sync.Object).SweepOnceAsync(CancellationToken.None);

        // Thirty and not a hundred and eighty: the tombstone is what the protocol must still be
        // able to tell a client gone a long time, the revision is what a human might want back.
        sync.Verify(s => s.PruneAsync(
            It.IsAny<DateTime>(),
            It.Is<DateTime>(d => WithinAnHourOf(d, DateTime.UtcNow.AddDays(-30))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnePass_LogsItsOutcomeEvenWhenItRemovedNothing()
    {
        var logger = new Mock<ILogger<CalendarTombstoneSweeper>>();

        await NewSweeper(Pruning(new PruneOutcome(0, 0)).Object, logger.Object)
            .SweepOnceAsync(CancellationToken.None);

        // Zero included, so the line doubles as the sweeper's heartbeat.
        logger.VerifyInformationLogged();
        logger.VerifyNoWarningLogged();
    }

    [Fact]
    public async Task OnePass_ThatHitItsCeiling_SaysSoOutLoud()
    {
        var logger = new Mock<ILogger<CalendarTombstoneSweeper>>();
        var capped = new PruneOutcome(CalendarSyncStore.MaxRowsPerSweep, 0, Capped: true);

        await NewSweeper(Pruning(capped).Object, logger.Object).SweepOnceAsync(CancellationToken.None);

        // The heartbeat reads as "everything old is gone" whatever the numbers on it, so a bounded
        // pass needs a line of its own: several in a row is a sweeper that has not been running.
        logger.VerifyWarningLoggedContaining("ceiling");
    }
}
