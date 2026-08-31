namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>A clock the test moves by hand, so TTL behaviour is exercised without sleeping.</summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private readonly List<HeldTimer> held = [];
    private readonly List<TimeSpan> requested = [];

    private DateTimeOffset now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now
    {
        get => now;
        set
        {
            now = value;
            FireDue();
        }
    }

    /// <summary>
    /// Off by default, so a wait taken through this clock is a real one and nothing that never
    /// asked for determinism changes behaviour. On, a wait completes only once <see cref="Now"/>
    /// has passed it — which is what lets a test assert a delay instead of chronometering it.
    /// </summary>
    public bool HoldTimers { get; set; }

    /// <summary>Every wait asked of this clock, held or not: what the code under test requested.</summary>
    public IReadOnlyList<TimeSpan> RequestedDelays
    {
        get { lock (requested) return [.. requested]; }
    }

    public int PendingTimers
    {
        get { lock (held) return held.Count; }
    }

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (requested) requested.Add(dueTime);
        if (!HoldTimers) return base.CreateTimer(callback, state, dueTime, period);

        var timer = new HeldTimer(this, callback, state, now + dueTime);
        lock (held) held.Add(timer);
        return timer;
    }

    /// <summary>Yields until a held wait is registered, so the test advances the clock after it began.</summary>
    public async Task WaitForPendingTimerAsync()
    {
        for (var attempt = 0; attempt < 10_000 && PendingTimers == 0; attempt++) await Task.Yield();
    }

    private void FireDue()
    {
        HeldTimer[] due;
        lock (held)
        {
            due = [.. held.Where(timer => timer.Due <= now)];
            foreach (var timer in due) held.Remove(timer);
        }

        // Outside the lock: the callback disposes its own timer, which comes back through Drop.
        foreach (var timer in due) timer.Fire();
    }

    private void Drop(HeldTimer timer)
    {
        lock (held) held.Remove(timer);
    }

    private sealed class HeldTimer(
        MutableTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
    {
        internal DateTimeOffset Due { get; private set; } = due;

        internal void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = owner.now + dueTime;
            return true;
        }

        public void Dispose() => owner.Drop(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
