namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// The clock a <see cref="Microsoft.Extensions.Caching.Memory.MemoryCache"/> reads its expirations
/// from, so a test can move time instead of waiting for it.
/// </summary>
internal sealed class StubSystemClock : Microsoft.Extensions.Internal.ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
