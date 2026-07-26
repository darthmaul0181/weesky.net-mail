using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class UserTests
{
    [Fact]
    public void Constructor_WithValidEmail_ParsesNameAndDomain()
    {
        var user = new User("john@example.com");

        Assert.Equal("john", user.Name);
        Assert.Equal("example.com", user.Domain);
    }

    [Fact]
    public void Constructor_WithNullEmail_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new User(null!));
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("a@b@c")]
    public void Constructor_WithInvalidEmail_ThrowsArgumentException(string email)
    {
        Assert.Throws<ArgumentException>(() => new User(email));
    }

    [Fact]
    public void Email_ReturnsNameAtDomain()
    {
        var user = new User("john@example.com");

        Assert.Equal("john@example.com", user.Email);
    }

    [Fact]
    public void PropertiesRemainAssignableAfterConstruction()
    {
        var user = new User("alice@test.com") { FullName = "Alice Smith" };

        Assert.Equal("alice@test.com", user.Email);
        Assert.Equal("Alice Smith", user.FullName);
    }
}
