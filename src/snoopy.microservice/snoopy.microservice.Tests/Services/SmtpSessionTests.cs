using MailKit.Net.Smtp;
using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SmtpSessionTests
{
    private static MimeMessage MessageFrom(string address)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("X", address));
        return message;
    }

    [Fact]
    public void DescribeFailure_NamesTheSenderOnASenderRejection()
    {
        var ex = new SmtpCommandException(SmtpErrorCode.SenderNotAccepted,
            SmtpStatusCode.MailboxNameNotAllowed, "denied");

        Assert.Equal("The mail server refused to send from michel@weesky.be",
            SmtpSession.DescribeFailure(ex, MessageFrom("michel@weesky.be")));
    }

    [Fact]
    public void DescribeFailure_StaysGenericForAnythingElse()
    {
        Assert.Equal("The mail server refused the message",
            SmtpSession.DescribeFailure(new InvalidOperationException("boom"), MessageFrom("a@b.c")));
    }
}
