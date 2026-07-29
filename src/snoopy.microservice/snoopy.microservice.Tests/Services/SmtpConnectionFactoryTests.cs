using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SmtpConnectionFactoryTests
{
    private static SmtpConnectionFactory CreateFactory()
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions());
        return new SmtpConnectionFactory(monitor.Object, NullLogger<SmtpConnectionFactory>.Instance);
    }

    [Fact]
    public async Task OpenAsync_FailsWhenTheConnectionCarriesNoSmtpHost()
    {
        var connection = TestConnections.Primary("a@b.c", "pw") with { SmtpHost = "" };

        var result = await CreateFactory().OpenAsync(connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Mail service is not configured", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ThrowsOnAMissingUsername()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateFactory().OpenAsync(TestConnections.Primary("", "pw"), CancellationToken.None));
    }
}
