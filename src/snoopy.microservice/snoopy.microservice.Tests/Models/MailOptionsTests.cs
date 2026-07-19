using MailKit.Security;
using Microsoft.Extensions.Configuration;
using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class MailOptionsTests
{
    private static MailOptions Bind(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new MailOptions();
        config.GetSection("Mail").Bind(options);
        return options;
    }

    [Fact]
    public void Defaults_AreTheHomeServerValues()
    {
        var options = new MailOptions();

        Assert.Equal(143, options.ImapPort);
        Assert.Equal(SecureSocketOptions.StartTls, options.ImapSecurity);
        Assert.Equal(587, options.SmtpPort);
        Assert.Equal(SecureSocketOptions.StartTls, options.SmtpSecurity);
        Assert.Equal(30, options.TimeoutSeconds);
        Assert.False(options.AllowInvalidCertificate);
    }

    [Fact]
    public void Bind_ReadsSecurityModeFromString()
    {
        var options = Bind(new()
        {
            ["Mail:ImapHost"] = "imap.example.org",
            ["Mail:ImapPort"] = "993",
            ["Mail:ImapSecurity"] = "SslOnConnect",
        });

        Assert.Equal("imap.example.org", options.ImapHost);
        Assert.Equal(993, options.ImapPort);
        Assert.Equal(SecureSocketOptions.SslOnConnect, options.ImapSecurity);
    }

    [Fact]
    public void IsImapConfigured_IsFalseWhenImapHostMissing()
    {
        Assert.False(new MailOptions { ImapHost = "" }.IsImapConfigured);
        Assert.True(new MailOptions { ImapHost = "mail.weesky.net" }.IsImapConfigured);
    }
}
