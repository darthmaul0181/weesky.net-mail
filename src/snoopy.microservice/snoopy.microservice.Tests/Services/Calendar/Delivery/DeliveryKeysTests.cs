using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryKeysTests
{
    [Fact]
    public void Generate_Is43Base64UrlCharacters_AndNeverRepeats()
    {
        var a = DeliveryKeys.Generate();
        var b = DeliveryKeys.Generate();
        Assert.Equal(43, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Matches_OnlyTheKeyThatWasHashed()
    {
        var key = DeliveryKeys.Generate();
        var hash = DeliveryKeys.Hash(key);
        Assert.Equal(32, hash.Length);
        Assert.True(DeliveryKeys.Matches(key, hash));
        Assert.False(DeliveryKeys.Matches(key + "x", hash));
        Assert.False(DeliveryKeys.Matches(null, hash));
        Assert.False(DeliveryKeys.Matches("", hash));
    }
}
