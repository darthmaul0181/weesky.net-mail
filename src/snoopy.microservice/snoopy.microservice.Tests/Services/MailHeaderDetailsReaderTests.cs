using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailHeaderDetailsReaderTests
{
    private static HeaderList Headers(params (string Field, string Value)[] entries)
    {
        var headers = new HeaderList();
        foreach (var (field, value) in entries) headers.Add(new Header(field, value));
        return headers;
    }

    [Fact]
    public void Parse_ReturnsAllNullsOnAMessageWithoutTheHeaders()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("Subject", "hello")));

        Assert.Null(result.MailingList);
        Assert.Null(result.SentBy);
        Assert.Null(result.SignedBy);
        Assert.Null(result.UnsubscribeUrl);
        Assert.Null(result.TlsReceived);
    }

    [Fact]
    public void Parse_ReadsTheMailingListVerbatim()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("List-Id", "Weesky news <news.weesky.net>")));

        Assert.Equal("Weesky news <news.weesky.net>", result.MailingList);
    }

    [Fact]
    public void Parse_ReadsSentByFromTheAuthenticatedEnvelope()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; spf=pass smtp.mailfrom=bounce@a547955.bnc3.mailjet.com")));

        Assert.Equal("a547955.bnc3.mailjet.com", result.SentBy);
    }

    [Fact]
    public void Parse_FallsBackToReturnPathForSentBy()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("Return-Path", "<bounce@list.example.org>")));

        Assert.Equal("list.example.org", result.SentBy);
    }

    [Fact]
    public void Parse_FallsBackToSenderForSentBy()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("Sender", "Weesky News <news@sender.example>")));

        Assert.Equal("sender.example", result.SentBy);
    }

    [Fact]
    public void Parse_PrefersTheAuthenticatedEnvelopeOverReturnPath()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; spf=pass smtp.mailfrom=bounce@authentic.example"),
            ("Return-Path", "<bounce@other.example>")));

        Assert.Equal("authentic.example", result.SentBy);
    }

    [Fact]
    public void Parse_PrefersReturnPathOverSenderForSentBy()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Return-Path", "<bounce@returnpath.example>"),
            ("Sender", "News <news@sender.example>")));

        Assert.Equal("returnpath.example", result.SentBy);
    }

    // Every header below the topmost was written upstream — or forged. Same rule as
    // MailAuthenticationReader: nothing is ever borrowed from a lower occurrence.
    [Fact]
    public void Parse_ReadsOnlyTheTopmostAuthenticationResults()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; dmarc=pass"),
            ("Authentication-Results", "relay.evil.example; spf=pass smtp.mailfrom=a@evil.example; dkim=pass header.d=evil.example")));

        Assert.Null(result.SentBy);
        Assert.Null(result.SignedBy);
    }

    [Fact]
    public void Parse_ReadsSignedByFromTheTopmostAuthenticationResults()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; dkim=pass header.d=google.com header.s=s1")));

        Assert.Equal("google.com", result.SignedBy);
    }

    [Fact]
    public void Parse_FallsBackToTheDkimSignatureForSignedBy()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("DKIM-Signature", "v=1; a=rsa-sha256; d=fondation-patrimoine.org; s=mailjet; h=from:to")));

        Assert.Equal("fondation-patrimoine.org", result.SignedBy);
    }

    [Fact]
    public void Parse_PicksTheHttpsUnsubscribeLinkOverMailto()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("List-Unsubscribe", "<mailto:unsub@x.be>, <https://x.be/unsub?id=1>")));

        Assert.Equal("https://x.be/unsub?id=1", result.UnsubscribeUrl);
    }

    [Fact]
    public void Parse_KeepsTheMailtoUnsubscribeWhenItIsAllThereIs()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("List-Unsubscribe", "<mailto:unsub@x.be>")));

        Assert.Equal("mailto:unsub@x.be", result.UnsubscribeUrl);
    }

    // The value is sender-controlled and lands in an <a href>; anything but http(s)/mailto is dropped.
    [Fact]
    public void Parse_DropsAnUnsubscribeCarryingNoSafeScheme()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("List-Unsubscribe", "<javascript:alert(1)>")));

        Assert.Null(result.UnsubscribeUrl);
    }

    [Fact]
    public void Parse_ReportsTlsFromTheTopmostReceived()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from out.mailjet.com by mx.weesky.net with ESMTPS id abc123")));

        Assert.True(result.TlsReceived);
    }

    [Fact]
    public void Parse_ReportsNoTlsOnAPlainSmtpHop()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from out.mailjet.com by mx.weesky.net with ESMTP id abc123")));

        Assert.False(result.TlsReceived);
    }

    // The topmost Received is the hop into our own server — the only one we wrote ourselves.
    [Fact]
    public void Parse_LetsTheTopmostReceivedWin()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from relay by mx.weesky.net with ESMTP id a"),
            ("Received", "from origin by relay with ESMTPS id b")));

        Assert.False(result.TlsReceived);
    }
}
