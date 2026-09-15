using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryRefusalsTests
{
    [Fact]
    public void TheFirstRefusalSpeaks_TheNextOnesWaitAMinute_ThenOneSpeaksForAll()
    {
        var clock = new MutableTimeProvider();
        var refusals = new DeliveryRefusals(clock);

        Assert.Equal(1, refusals.Note());
        Assert.Null(refusals.Note());
        Assert.Null(refusals.Note());
        clock.Now = clock.Now.AddSeconds(59);
        Assert.Null(refusals.Note());
        clock.Now = clock.Now.AddSeconds(2);
        Assert.Equal(4, refusals.Note());
        Assert.Null(refusals.Note());
    }

    [Fact]
    public void TheOverloadCounter_ThrottlesTheSameWay_IndependentlyOfTheKeyCounter()
    {
        var clock = new MutableTimeProvider();
        var refusals = new DeliveryRefusals(clock);

        Assert.Equal(1, refusals.NoteOverload());
        Assert.Null(refusals.NoteOverload());
        // A burst of key refusals in between speaks on its own window and never resets the
        // overload counter's: the two counters must not share state.
        Assert.Equal(1, refusals.Note());
        Assert.Null(refusals.NoteOverload());
        clock.Now = clock.Now.AddMinutes(1).AddSeconds(1);
        Assert.Equal(3, refusals.NoteOverload());
        Assert.Null(refusals.NoteOverload());
    }
}
