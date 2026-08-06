using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class StagedAttachmentSweeperTests
{
    private readonly Mock<IStagedAttachmentStore> _store = new();

    // Tests default the startup jitter to zero: they assert the sweep runs promptly, not that it
    // waits out the real staggering delay production uses.
    private StagedAttachmentSweeper CreateSweeper(TimeSpan? startupJitterMax = null) =>
        new(_store.Object, NullLogger<StagedAttachmentSweeper>.Instance, startupJitterMax ?? TimeSpan.Zero);

    // The bug this guards against: a restart drops the in-memory staged entries, so every
    // previously staged file becomes an orphan the hourly PeriodicTimer could only reclaim later.
    [Fact]
    public async Task ExecuteAsync_SweepsAtStartup_BeforeTheFirstPeriodElapses()
    {
        var swept = new TaskCompletionSource();
        _store.Setup(s => s.SweepExpired())
              .Returns(0)
              .Callback(() => swept.TrySetResult());

        var sweeper = CreateSweeper();
        await sweeper.StartAsync(CancellationToken.None);
        try
        {
            var completed = await Task.WhenAny(swept.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(swept.Task, completed);
        }
        finally
        {
            await sweeper.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSweepThrows_DoesNotCrashTheLoop()
    {
        var callCount = 0;
        _store.Setup(s => s.SweepExpired())
              .Callback(() => callCount++)
              .Throws(new InvalidOperationException("boom"));

        var sweeper = CreateSweeper();
        await sweeper.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.True(callCount >= 1);
        }
        finally
        {
            await sweeper.StopAsync(CancellationToken.None);
        }
    }
}
