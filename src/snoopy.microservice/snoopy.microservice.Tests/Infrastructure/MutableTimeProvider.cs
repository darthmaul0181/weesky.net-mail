namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>A clock the test moves by hand, so TTL behaviour is exercised without sleeping.</summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
