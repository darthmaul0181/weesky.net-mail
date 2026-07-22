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

    // Anything below the hop into our own server could have been forged by the sender, so a
    // lower ESMTPS claim never upgrades a plain hop.
    [Fact]
    public void Parse_LetsTheTopmostNetworkHopWin()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from relay by mx.weesky.net with ESMTP id a"),
            ("Received", "from origin by relay with ESMTPS id b")));

        Assert.False(result.TlsReceived);
    }

    // Dovecot's handoff to the mailbox runs on a local socket and carries no transport security;
    // reading it instead of the hop below reported "no encryption" on every delivered message.
    [Fact]
    public void Parse_SkipsTheLocalDeliveryHopWhenReadingTls()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from mail.weesky.net\n\tby mail.weesky.net with LMTP\n\tid QMLkIi5tYGrC9QEA"),
            ("Received", "from o751.wrm1.useinsider.email (o751.wrm1.useinsider.email [149.72.191.208])\n"
                         + "\t(using TLSv1.3 with cipher TLS_AES_128_GCM_SHA256 (128/128 bits))\n"
                         + "\tby mail.weesky.net (Postfix) with ESMTPS id 2D4142857C")));

        Assert.True(result.TlsReceived);
    }

    [Fact]
    public void Parse_SkipsPostfixLocalDeliveryToo()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "by mail.weesky.net (Postfix) with local id a"),
            ("Received", "from origin by mail.weesky.net (Postfix) with ESMTPS id b")));

        Assert.True(result.TlsReceived);
    }

    // Skipping the local hop must land on the network hop, not hunt down the chain for a TLS claim.
    [Fact]
    public void Parse_StopsAtThePlainNetworkHopBelowALocalDelivery()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from mail.weesky.net by mail.weesky.net with LMTP id a"),
            ("Received", "from relay by mail.weesky.net (Postfix) with ESMTP id b"),
            ("Received", "from origin by relay with ESMTPS id c")));

        Assert.False(result.TlsReceived);
    }

    [Fact]
    public void Parse_ReportsNoTlsWhenEveryHopIsALocalDelivery()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from mail.weesky.net by mail.weesky.net with LMTP id a")));

        Assert.Null(result.TlsReceived);
    }

    // RFC 2369 delimits entries with <> precisely because a URL may legally contain commas.
    [Fact]
    public void Parse_KeepsACommaInsideABracketedUnsubscribeUrl()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("List-Unsubscribe", "<https://x.be/unsub?ids=1,2>")));

        Assert.Equal("https://x.be/unsub?ids=1,2", result.UnsubscribeUrl);
    }

    [Fact]
    public void Parse_KeepsAMultiRecipientMailtoIntact()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("List-Unsubscribe", "<mailto:a@x.be,b@x.be>")));

        Assert.Equal("mailto:a@x.be,b@x.be", result.UnsubscribeUrl);
    }

    [Fact]
    public void Parse_StillSplitsABracketlessUnsubscribeOnCommas()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("List-Unsubscribe", "mailto:unsub@x.be, https://x.be/unsub")));

        Assert.Equal("https://x.be/unsub", result.UnsubscribeUrl);
    }

    // The frontend branches on the scheme case-sensitively; the wire scheme is case-insensitive.
    [Fact]
    public void Parse_LowercasesTheUnsubscribeScheme()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("List-Unsubscribe", "<MAILTO:Unsub@X.be>")));

        Assert.Equal("mailto:Unsub@X.be", result.UnsubscribeUrl);
    }

    [Fact]
    public void Parse_DoesNotMistakeTlsInsideAQueueIdForEncryption()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from relay by mx.weesky.net with ESMTP id ABTLSQ7")));

        Assert.False(result.TlsReceived);
    }

    [Fact]
    public void Parse_ReadsTlsVersionNotes()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from relay by mx.weesky.net with ESMTP (version=TLS1_2 cipher=ECDHE-RSA-AES256) id a")));

        Assert.True(result.TlsReceived);
    }

    [Fact]
    public void Parse_ReadsALowercaseEsmtpsaDialect()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from relay by mx.weesky.net with esmtpsa id a")));

        Assert.True(result.TlsReceived);
    }

    // A line claiming "signed by X" must not survive a failed verification — no fallback either.
    [Fact]
    public void Parse_HidesSignedByWhenTheSignatureFailed()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; dkim=fail header.d=paypal.com"),
            ("DKIM-Signature", "v=1; a=rsa-sha256; d=paypal.com; s=s1")));

        Assert.Null(result.SignedBy);
    }

    // A mailing list breaks the original signature while its own verifies: name the passing signer.
    [Fact]
    public void Parse_NamesThePassingSignerAmongFailures()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; dkim=fail header.d=original.example; dkim=pass header.d=list.example")));

        Assert.Equal("list.example", result.SignedBy);
    }
}
