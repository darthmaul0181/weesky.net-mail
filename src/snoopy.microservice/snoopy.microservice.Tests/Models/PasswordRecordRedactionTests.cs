using System.Text;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

/// <summary>
/// A positional record's synthesised ToString prints every member, so one {Payload} in a log
/// template dumps a live password. Every record carrying one overrides it, as SieveConnection and
/// MailAccountConnection already did.
/// </summary>
public sealed class PasswordRecordRedactionTests
{
    [Fact]
    public void MailCredentialPayload_ToString_NeverPrintsThePassword()
    {
        var text = new MailCredentialPayload("s3cret-mail", Encoding.UTF8.GetBytes("kek")).ToString();

        Assert.DoesNotContain("s3cret-mail", text, StringComparison.Ordinal);
        Assert.DoesNotContain("kek", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectAccountRequest_ToString_NeverPrintsThePassword()
    {
        var domainId = Guid.NewGuid();

        var text = new ConnectAccountRequest(domainId, "bob@external.test", "s3cret-connect").ToString();

        Assert.DoesNotContain("s3cret-connect", text, StringComparison.Ordinal);
        Assert.Contains("bob@external.test", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectedAccountPasswordRequest_ToString_NeverPrintsThePassword()
    {
        var text = new ConnectedAccountPasswordRequest("s3cret-reentered").ToString();

        Assert.DoesNotContain("s3cret-reentered", text, StringComparison.Ordinal);
    }
}
