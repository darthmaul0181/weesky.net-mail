using Microsoft.AspNetCore.DataProtection;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ClientSecretProtectorTests
{
    private static ClientSecretProtector Create() =>
        new(DataProtectionProvider.Create(nameof(ClientSecretProtectorTests)));

    [Fact]
    public void Protect_RoundTrips()
    {
        var protector = Create();

        Assert.Equal("s3cr3t", protector.Unprotect(protector.Protect("s3cr3t")));
    }

    [Fact]
    public void Protect_ProducesSomethingThatIsNotThePlaintext()
    {
        var protector = Create();

        Assert.DoesNotContain("s3cr3t", System.Text.Encoding.UTF8.GetString(protector.Protect("s3cr3t")));
    }

    [Fact]
    public void Unprotect_OfRubbish_AnswersNull()
    {
        Assert.Null(Create().Unprotect([1, 2, 3, 4]));
    }
}
