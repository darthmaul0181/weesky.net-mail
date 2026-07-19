using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class AliasTests
{
    private static Alias Make(string name = "test", string domain = "example.com")
        => new() { Name = name, Domain = domain };

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
        => Assert.True(Make().Equals(Make()));

    [Fact]
    public void Equals_IsCaseInsensitive()
        => Assert.True(Make("Test", "Example.COM").Equals(Make("test", "example.com")));

    [Fact]
    public void Equals_DifferentName_ReturnsFalse()
        => Assert.False(Make("a").Equals(Make("b")));

    [Fact]
    public void Equals_DifferentDomain_ReturnsFalse()
        => Assert.False(Make(domain: "a.com").Equals(Make(domain: "b.com")));

    [Fact]
    public void Equals_Null_ReturnsFalse()
        => Assert.False(Make().Equals(null));

    [Fact]
    public void Equals_DifferentType_ReturnsFalse()
        => Assert.False(Make().Equals("not an alias"));

    [Fact]
    public void GetHashCode_IsBasedOnEmail()
        => Assert.Equal("test@example.com".GetHashCode(), Make().GetHashCode());

    [Fact]
    public void DebuggerDisplay_ReturnsEmailFormat()
        => Assert.Equal("test@example.com", Make().DebuggerDisplay);
}
