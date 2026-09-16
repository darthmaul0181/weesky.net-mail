using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Platform;

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
