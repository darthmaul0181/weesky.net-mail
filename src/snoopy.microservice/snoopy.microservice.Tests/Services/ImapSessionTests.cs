using MailKit;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ImapSessionTests
{
    [Fact]
    public void FillSummary_TranscribesTheEnvelopeRecipients()
    {
        var envelope = new Envelope();
        envelope.To.Add(new MailboxAddress("Bob", "bob@ext.example"));
        envelope.To.Add(new MailboxAddress(string.Empty, "carol@ext.example"));
        var item = new Mock<IMessageSummary>();
        item.SetupGet(i => i.UniqueId).Returns(new UniqueId(7));
        item.SetupGet(i => i.Envelope).Returns(envelope);

        var summary = ImapSession.FillSummary(new MailMessageSummary(), item.Object);

        Assert.Equal(2, summary.To.Count);
        Assert.Equal("Bob", summary.To[0].Name);
        Assert.Equal("bob@ext.example", summary.To[0].Address);
        Assert.Equal("carol@ext.example", summary.To[1].Address);
    }

    [Fact]
    public void FillSummary_LeavesToEmptyWithoutAnEnvelope()
    {
        var item = new Mock<IMessageSummary>();
        item.SetupGet(i => i.UniqueId).Returns(new UniqueId(7));
        item.SetupGet(i => i.Envelope).Returns((Envelope?)null);

        var summary = ImapSession.FillSummary(new MailMessageSummary(), item.Object);

        Assert.Empty(summary.To);
    }

    /// <summary>A fetched summary carrying the headers the priority is read out of.</summary>
    private static IMessageSummary FakeSummary(HeaderList? headers)
    {
        var item = new Mock<IMessageSummary>();
        item.SetupGet(i => i.UniqueId).Returns(new UniqueId(7));
        item.SetupGet(i => i.Headers).Returns(headers!);
        return item.Object;
    }

    /// <summary>The list row's marker comes from here, and search hits share the mapping.</summary>
    [Fact]
    public void FillSummary_ReadsThePriorityOffTheFetchedHeaders()
    {
        var headers = new HeaderList();
        headers.Add("X-Priority", "1 (Highest)");

        var summary = ImapSession.FillSummary(new MailMessageSummary(), FakeSummary(headers));

        Assert.Equal(MailPriority.High, summary.Priority);
    }

    [Fact]
    public void FillSummary_IsNormalWhenTheMessageCarriesNoPriorityHeader()
    {
        var summary = ImapSession.FillSummary(new MailMessageSummary(), FakeSummary(new HeaderList()));

        Assert.Equal(MailPriority.Normal, summary.Priority);
    }

    /// <summary>A server that answered without the header set must not throw or invent a priority.</summary>
    [Fact]
    public void FillSummary_IsNormalWhenTheServerReturnedNoHeadersAtAll()
    {
        var summary = ImapSession.FillSummary(new MailMessageSummary(), FakeSummary(headers: null));

        Assert.Equal(MailPriority.Normal, summary.Priority);
    }

    [Theory]
    [InlineData("INBOX", '/', null)]
    [InlineData("INBOX/Projects", '/', "INBOX")]
    [InlineData("INBOX/Projects/Alpha", '/', "INBOX/Projects")]
    [InlineData("INBOX.Projects", '.', "INBOX")]
    [InlineData("/leading", '/', null)]
    public void ParentPath_TrimsTheLastSegment(string fullName, char separator, string? expected)
    {
        Assert.Equal(expected, ImapSession.ParentPath(fullName, separator));
    }

    [Theory]
    [InlineData("Projects", '/', true)]
    [InlineData("Pro/jects", '/', false)]
    [InlineData("Pro.jects", '.', false)]
    [InlineData("Pro.jects", '/', true)]
    [InlineData("", '/', false)]
    [InlineData("   ", '/', false)]
    public void IsValidLeafName_RejectsSeparatorsAndBlanks(string name, char separator, bool expected)
    {
        Assert.Equal(expected, ImapSession.IsValidLeafName(name, separator));
    }

    [Theory]
    [InlineData("", "Projects", '/', "Projects")]
    [InlineData("INBOX", "Projects", '/', "INBOX/Projects")]
    [InlineData("INBOX", "Projects", '.', "INBOX.Projects")]
    public void CombinePath_JoinsWithTheServerSeparator(string parent, string name, char separator, string expected)
    {
        Assert.Equal(expected, ImapSession.CombinePath(parent, name, separator));
    }

    // ── Address info conversion ──────────────────────────────────────

    [Fact]
    public void ToAddressInfos_PreservesDisplayNameWhenPresent()
    {
        var addresses = new InternetAddressList { new MailboxAddress("Bob Smith", "bob@example.com") };

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Single(result);
        Assert.Equal("Bob Smith", result[0].Name);
        Assert.Equal("bob@example.com", result[0].Address);
    }

    [Fact]
    public void ToAddressInfos_UsesEmptyStringWhenDisplayNameIsNull()
    {
        var addresses = new InternetAddressList { new MailboxAddress(null, "alice@example.com") };

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Single(result);
        Assert.Equal(string.Empty, result[0].Name);
        Assert.Equal("alice@example.com", result[0].Address);
    }

    [Fact]
    public void ToAddressInfos_ReturnsEmptyListForNullAddressList()
    {
        var result = ImapSession.ToAddressInfos(null);

        Assert.Empty(result);
    }

    [Fact]
    public void ToAddressInfos_ReturnsEmptyListForEmptyAddressList()
    {
        var addresses = new InternetAddressList();

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Empty(result);
    }

    [Fact]
    public void ToAddressInfos_PreservesOrderWithMultipleRecipients()
    {
        var addresses = new InternetAddressList
        {
            new MailboxAddress("Charlie", "charlie@example.com"),
            new MailboxAddress("Diana", "diana@example.com"),
            new MailboxAddress(null, "eve@example.com")
        };

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Equal(3, result.Count);
        Assert.Equal("Charlie", result[0].Name);
        Assert.Equal("charlie@example.com", result[0].Address);
        Assert.Equal("Diana", result[1].Name);
        Assert.Equal("diana@example.com", result[1].Address);
        Assert.Equal(string.Empty, result[2].Name);
        Assert.Equal("eve@example.com", result[2].Address);
    }

    [Fact]
    public void ApplyThreading_TranscribesTheHeaders()
    {
        var message = new MimeMessage();
        message.MessageId = "current@id";
        message.InReplyTo = "parent@id";
        message.References.Add("grandparent@id");
        message.References.Add("parent@id");
        message.ReplyTo.Add(new MailboxAddress("List", "list@x.example"));
        message.Bcc.Add(new MailboxAddress("Hidden", "bcc@x.example"));

        var detail = new MailMessageDetail();
        ImapSession.ApplyThreading(detail, message);

        Assert.Equal("current@id", detail.MessageId);
        Assert.Equal("parent@id", detail.InReplyTo);
        Assert.Equal(new[] { "grandparent@id", "parent@id" }, detail.References);
        Assert.Equal("list@x.example", Assert.Single(detail.ReplyTo).Address);
        Assert.Equal("bcc@x.example", Assert.Single(detail.Bcc).Address);
    }

    [Fact]
    public void ApplyThreading_DefaultsToNullAndEmptyWhenAbsent()
    {
        // MimeMessage's constructor generates a Message-Id — remove it to model a header-less original.
        var message = new MimeMessage();
        message.Headers.Remove(HeaderId.MessageId);

        var detail = new MailMessageDetail();
        ImapSession.ApplyThreading(detail, message);

        Assert.Null(detail.MessageId);
        Assert.Null(detail.InReplyTo);
        Assert.Empty(detail.References);
        Assert.Empty(detail.ReplyTo);
        Assert.Empty(detail.Bcc);
    }

    // ── Content-Id normalisation ─────────────────────────────────────
    //
    // GetMessageAsync itself is not unit-testable: it drives a real MailKit ImapClient
    // (ImapSession wraps a concrete ImapClient, not an interface) through GetFolderAsync/
    // FetchAsync/GetMessageAsync, none of which can be produced from a fixture without a
    // live or fake IMAP server. The transcription this task adds is the pure normalisation
    // step, exercised here directly instead — the closest seam the rest of this suite
    // already uses for ImapSession's other pure static helpers (ApplyThreading, ToAddressInfos, ...).

    [Fact]
    public void TrimAngleBrackets_StripsAngleBracketsFromAServerReportedContentId()
    {
        Assert.Equal("logo@mail", ImapSession.TrimAngleBrackets("<logo@mail>"));
    }

    [Fact]
    public void TrimAngleBrackets_LeavesABareContentIdUnchanged()
    {
        Assert.Equal("logo@mail", ImapSession.TrimAngleBrackets("logo@mail"));
    }

    [Fact]
    public void TrimAngleBrackets_ReturnsNullForNull()
    {
        Assert.Null(ImapSession.TrimAngleBrackets(null));
    }

    [Fact]
    public void TrimAngleBrackets_ReturnsNullForWhitespaceOrEmpty()
    {
        Assert.Null(ImapSession.TrimAngleBrackets(string.Empty));
        Assert.Null(ImapSession.TrimAngleBrackets("   "));
    }

    // ── Which body parts reach the client ────────────────────────────
    //
    // Same seam as above: the enumeration itself needs a live FETCH, the decision it makes does
    // not. A Vaultwarden logo arrives inline, unnamed, with a Content-ID; dropping it left the
    // reader holding a cid: with nothing to resolve it against.

    private static BodyPartBasic Part(
        string? contentId = null, string? fileName = null, bool attachment = false) =>
        new(new ContentType("image", "png"), "1")
        {
            ContentId = contentId,
            ContentDisposition = attachment || fileName != null
                ? new ContentDisposition(attachment ? "attachment" : "inline") { FileName = fileName }
                : null
        };

    [Fact]
    public void IsListedPart_KeepsAnUnnamedInlinePartCarryingAContentId()
    {
        Assert.True(ImapSession.IsListedPart(Part(contentId: "<logo@mail>")));
    }

    [Fact]
    public void IsListedPart_KeepsAnAttachmentAndANamedPart()
    {
        Assert.True(ImapSession.IsListedPart(Part(attachment: true)));
        Assert.True(ImapSession.IsListedPart(Part(fileName: "photo.png")));
    }

    // The message's own text and html carry none of the three, and must not read as attachments.
    [Fact]
    public void IsListedPart_DropsAPartThatIsNeitherAttachedNorNamedNorReferenced()
    {
        Assert.False(ImapSession.IsListedPart(Part()));
    }

    [Fact]
    public void IsListedPart_DropsAPartWhoseContentIdIsEmptyBrackets()
    {
        Assert.False(ImapSession.IsListedPart(Part(contentId: "<>")));
    }
}
