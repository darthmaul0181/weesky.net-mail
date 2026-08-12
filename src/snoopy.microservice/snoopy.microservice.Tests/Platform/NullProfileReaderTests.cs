using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Platform;

public sealed class NullProfileReaderTests
{
    [Fact]
    public async Task GetDisplayNameAsync_IsNull()
    {
        var name = await new NullProfileReader().GetDisplayNameAsync(
            new User("mick@weesky.be") { FullName = "Mick" }, CancellationToken.None);

        Assert.Null(name);
    }
}
