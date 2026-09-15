namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

/// <summary>Refused delivery calls are what the Internet can multiply: they get one log line a
/// minute at most, carrying the count since the last one (spec 5e3, décision 8). Two independent
/// counters share this one instance — a wrong or missing key (<see cref="Note"/>), and the
/// concurrency limiter's own 503 (<see cref="NoteOverload"/>, décision 13) — each with its own
/// window, so a burst of one never silences a report of the other. A singleton.
/// Public: a public MVC controller (<see cref="Controllers.DeliveryController"/>) injects it, and
/// an internal type there would not compile (CS0051) — the same reason IDeliveryReplyApplier is
/// public rather than internal.</summary>
public sealed class DeliveryRefusals(TimeProvider clock)
{
    private readonly Counter keyRefusals = new(clock);
    private readonly Counter overloadRefusals = new(clock);

    /// <summary>The number of wrong-or-missing-key refusals to report now, or null to stay silent.</summary>
    internal int? Note() => keyRefusals.Note();

    /// <summary>The number of concurrency-limiter refusals to report now, or null to stay silent.</summary>
    internal int? NoteOverload() => overloadRefusals.Note();

    /// <summary>One counter, one lock, one minute-wide window — the shape both refusals share.</summary>
    private sealed class Counter(TimeProvider clock)
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
        private readonly Lock gate = new();
        private DateTimeOffset? lastSpoken;
        private int pending;

        internal int? Note()
        {
            lock (gate)
            {
                pending++;
                var now = clock.GetUtcNow();
                if (lastSpoken is { } spoken && now - spoken < Window) return null;
                lastSpoken = now;
                var count = pending;
                pending = 0;
                return count;
            }
        }
    }
}
