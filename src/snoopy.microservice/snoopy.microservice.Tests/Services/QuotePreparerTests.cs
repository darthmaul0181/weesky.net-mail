using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class QuotePreparerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"quote-tests-{Guid.NewGuid():N}");
    private readonly StagedAttachmentStore _store;
    private readonly QuotePreparer _preparer;

    public QuotePreparerTests()
    {
        _store = CreateStore(maxMessageSizeMb: 25);
        _preparer = new QuotePreparer(new OutgoingMailSanitizer(), _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* nothing staged */ }
    }

    private StagedAttachmentStore CreateStore(int maxMessageSizeMb)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue)
            .Returns(new MailOptions { MaxMessageSizeMb = maxMessageSizeMb, StagedAttachmentTtlHours = 12 });
        return new StagedAttachmentStore(
            monitor.Object, TimeProvider.System, NullLogger<StagedAttachmentStore>.Instance, _root);
    }

    private static MimeMessage MessageWithInlineImageAndPdf()
    {
        var builder = new BodyBuilder
        {
            HtmlBody = "<p>Hello</p><img src=\"cid:logo@mail\"><script>alert(1)</script>",
        };
        var image = builder.LinkedResources.Add("logo.png", new byte[] { 1, 2, 3 }, new ContentType("image", "png"));
        image.ContentId = "logo@mail";
        builder.Attachments.Add("report.pdf", new byte[] { 9, 9 }, new ContentType("application", "pdf"));
        return new MimeMessage { Body = builder.ToMessageBody() };
    }

    [Fact]
    public async Task Reply_StagesInlineImagesAndRewritesTheirSrc_ButNoAttachments()
    {
        var result = await _preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var staged = Assert.Single(result.Value.Attachments);
        Assert.Equal("logo@mail", staged.ContentId);
        Assert.Contains($"/api/Mail/Attachments/{staged.Id}/content", result.Value.QuotableHtml);
        Assert.DoesNotContain("cid:", result.Value.QuotableHtml);
        Assert.DoesNotContain("script", result.Value.QuotableHtml);
    }

    [Fact]
    public async Task Forward_AlsoRestagesTheRealAttachments()
    {
        var result = await _preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.Forward, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Attachments.Count);
        Assert.Contains(result.Value.Attachments, a => a.ContentId == "logo@mail");
        Assert.Contains(result.Value.Attachments, a => a.ContentId == null && a.FileName == "report.pdf");
    }

    [Fact]
    public async Task EditAsNew_BehavesExactlyLikeForward()
    {
        var result = await _preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.EditAsNew, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Attachments.Count);
    }

    [Fact]
    public async Task TextOnlyOriginal_IsEscapedWithLineBreaks()
    {
        var message = new MimeMessage { Body = new TextPart("plain") { Text = "a < b\nsecond line" } };

        var result = await _preparer.PrepareAsync("acc", message, QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("a &lt; b<br>second line", result.Value.QuotableHtml);
        Assert.Empty(result.Value.Attachments);
    }

    [Fact]
    public async Task ACidWithNoMatchingImagePart_LosesItsImg()
    {
        var builder = new BodyBuilder { HtmlBody = "<p>Hi</p><img src=\"cid:gone@mail\">" };
        var message = new MimeMessage { Body = builder.ToMessageBody() };

        var result = await _preparer.PrepareAsync("acc", message, QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("cid:", result.Value.QuotableHtml);
        Assert.DoesNotContain("<img", result.Value.QuotableHtml);
        Assert.Empty(result.Value.Attachments);
    }

    [Fact]
    public async Task TheSameCidTwice_IsStagedOnceAndRewrittenEverywhere()
    {
        var builder = new BodyBuilder { HtmlBody = "<img src=\"cid:logo@mail\"><p>x</p><img src=\"cid:logo@mail\">" };
        var image = builder.LinkedResources.Add("logo.png", new byte[] { 1, 2, 3 }, new ContentType("image", "png"));
        image.ContentId = "logo@mail";
        var message = new MimeMessage { Body = builder.ToMessageBody() };

        var result = await _preparer.PrepareAsync("acc", message, QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var staged = Assert.Single(result.Value.Attachments);
        var url = $"/api/Mail/Attachments/{staged.Id}/content";
        Assert.Equal(2, result.Value.QuotableHtml.Split(url).Length - 1);
    }

    [Fact]
    public async Task AnInlineImageAlsoMarkedAsAnAttachment_IsStagedOnceOnForward()
    {
        var image = new MimePart("image", "png")
        {
            Content = new MimeContent(new MemoryStream([1, 2, 3])),
            ContentId = "logo@mail",
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "logo.png" },
        };
        var message = new MimeMessage
        {
            Body = new Multipart("mixed") { new TextPart("html") { Text = "<img src=\"cid:logo@mail\">" }, image },
        };

        var result = await _preparer.PrepareAsync("acc", message, QuotePurpose.Forward, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var staged = Assert.Single(result.Value.Attachments);
        Assert.Equal("logo@mail", staged.ContentId);
    }

    [Fact]
    public async Task AnAttachedMessage_IsStagedAsAStandaloneEml()
    {
        var inner = new MimeMessage { Subject = "The inner subject" };
        inner.From.Add(new MailboxAddress("Sender", "sender@example.test"));
        inner.To.Add(new MailboxAddress("Recipient", "recipient@example.test"));
        inner.Body = new TextPart("plain") { Text = "inner body" };
        var attached = new MessagePart
        {
            Message = inner,
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "inner.eml" },
        };
        var message = new MimeMessage
        {
            Body = new Multipart("mixed") { new TextPart("html") { Text = "<p>See attached</p>" }, attached },
        };

        var result = await _preparer.PrepareAsync("acc", message, QuotePurpose.Forward, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var staged = Assert.Single(result.Value.Attachments);
        Assert.Equal("inner.eml", staged.FileName);

        var opened = _store.Open("acc", staged.Id);
        Assert.True(opened.IsSuccess);
        var parsed = await MimeMessage.LoadAsync(opened.Value.FilePath, CancellationToken.None);
        Assert.Equal("The inner subject", parsed.Subject);
    }

    [Fact]
    public async Task AStagingFailure_FailsTheWholePreparation()
    {
        // A 0 MB cap makes every SaveAsync fail — the store's own refusal must surface.
        var preparer = new QuotePreparer(new OutgoingMailSanitizer(), CreateStore(maxMessageSizeMb: 0));

        var result = await preparer.PrepareAsync("acc", MessageWithInlineImageAndPdf(), QuotePurpose.Reply, CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
