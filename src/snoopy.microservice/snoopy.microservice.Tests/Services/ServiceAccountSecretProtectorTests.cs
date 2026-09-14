using Microsoft.AspNetCore.DataProtection;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ServiceAccountSecretProtectorTests
{
    private static readonly IDataProtectionProvider KeyRing =
        DataProtectionProvider.Create(nameof(ServiceAccountSecretProtectorTests));

    [Fact]
    public void Protect_RoundTrips()
    {
        var protector = new ServiceAccountSecretProtector(KeyRing);

        Assert.Equal("smtp-p4ss", protector.Unprotect(protector.Protect("smtp-p4ss")));
    }

    [Fact]
    public void Protect_ProducesSomethingThatIsNotThePlaintext()
    {
        var blob = new ServiceAccountSecretProtector(KeyRing).Protect("smtp-p4ss");

        Assert.DoesNotContain("smtp-p4ss", System.Text.Encoding.UTF8.GetString(blob));
    }

    // Same key ring, different purposes: neither secret's blob can be replayed as the other.
    [Fact]
    public void TheTwoSecrets_DoNotOpenEachOther()
    {
        var serviceAccount = new ServiceAccountSecretProtector(KeyRing);
        var clientSecret = new ClientSecretProtector(KeyRing);

        Assert.NotEqual(ServiceAccountSecretProtector.Purpose, ClientSecretProtector.Purpose);
        Assert.Null(clientSecret.Unprotect(serviceAccount.Protect("smtp-p4ss")));
        Assert.Null(serviceAccount.Unprotect(clientSecret.Protect("oauth-s3cr3t")));
    }

    [Fact]
    public void Unprotect_OfACorruptedBlob_AnswersNull()
    {
        var protector = new ServiceAccountSecretProtector(KeyRing);
        var blob = protector.Protect("smtp-p4ss");
        blob[^1] ^= 0xFF;

        Assert.Null(protector.Unprotect(blob));
    }
}
