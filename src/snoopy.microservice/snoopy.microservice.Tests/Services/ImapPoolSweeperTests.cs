using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ImapPoolSweeperTests
{
    private readonly Mock<IImapConnectionPool> _pool = new();
    private readonly Mock<ILogger<ImapPoolSweeper>> _logger = new();

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(10);
        }
        return true;
    }

    [Fact]
    public async Task ExecuteAsync_SweepsThePoolEveryPeriod()
    {
        var sweeps = 0;
        _pool.Setup(p => p.SweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { sweeps++; return 0; });
        using var sweeper = new ImapPoolSweeper(_pool.Object, _logger.Object, TimeSpan.FromMilliseconds(20));

        await sweeper.StartAsync(CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => sweeps >= 3));
        await sweeper.StopAsync(CancellationToken.None);
    }

    // A pass that throws must not end the loop: the next tick retries.
    [Fact]
    public async Task ExecuteAsync_SurvivesAFailingPass()
    {
        var calls = 0;
        _pool.Setup(p => p.SweepAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(() => ++calls == 1 ? throw new InvalidOperationException("boom") : 0);
        using var sweeper = new ImapPoolSweeper(_pool.Object, _logger.Object, TimeSpan.FromMilliseconds(20));

        await sweeper.StartAsync(CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => calls >= 3));
        await sweeper.StopAsync(CancellationToken.None);
    }

    // Counters, not events: one aggregate line per PassesPerReport passes, none in between.
    [Fact]
    public async Task ExecuteAsync_LogsOneAggregateLinePerReportInterval()
    {
        var sweeps = 0;
        _pool.Setup(p => p.SweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { sweeps++; return 0; });
        _pool.Setup(p => p.Snapshot()).Returns(new PoolStatistics(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12));
        using var sweeper = new ImapPoolSweeper(_pool.Object, _logger.Object, TimeSpan.FromMilliseconds(5));

        await sweeper.StartAsync(CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => sweeps >= ImapPoolSweeper.PassesPerReport));
        await sweeper.StopAsync(CancellationToken.None);

        // Every counter is on the line, in the order Snapshot returns them: a field added to
        // PoolStatistics and forgotten in Report is invisible in production and shows up here.
        _logger.Verify(l => l.Log(
                LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString() ==
                    "IMAP pool: 1 idle, 2 borrowed, 3 keys; 4 borrows, 5 reused, 6 opened, 7 single-use, " +
                    "8 health failures, 9 closed idle, 10 closed lifetime, 11 closed at return, 12 evicted"),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
        _logger.Verify(l => l.Log(
                LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("IMAP pool")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtMost(sweeps / ImapPoolSweeper.PassesPerReport + 1));
    }
}
