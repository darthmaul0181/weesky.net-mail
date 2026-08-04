using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using MimeKit.Utils;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class MailComposeControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "hunter2");
    private static readonly string StagedScope = MailAccountConnection.StagedScope(
        new User("alice@weesky.be") { WebmailUid = WebmailUid }, MailAccountConnection.Primary);

    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();
    private readonly Mock<IMailSender> _sender = new();
    private readonly Mock<IQuotePreparer> _quotes = new();
    private readonly Mock<IDraftSaver> _drafts = new();

    private MailComposeController CreateController()
    {
        ResolveTo(Conn);

        return new MailComposeController(_messages.Object, _connections.Object, _sender.Object,
                                         _quotes.Object, _drafts.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", WebmailUid)
        };
    }

    /// <summary>Moq resolves overlapping setups by recency: call after <c>CreateController()</c>.</summary>
    private void ResolveTo(MailAccountConnection connection)
        => _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Result.Success(connection));

    private void FailResolution(string error)
        => _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Result.Failure<MailAccountConnection>(error));

    // ── Send ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WithExplicitlyNullAttachmentIds_DoesNotThrow()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new SendMessageResult(true)));

        var result = await CreateController().SendMessage(
            new SendMessageRequest { To = ["bob@weesky.be"], AttachmentIds = null! }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        _sender.Verify(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
            It.Is<SendMessageRequest>(r => r.AttachmentIds != null && r.AttachmentIds.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessage_RefusesWithoutARecipient()
    {
        var result = await CreateController().SendMessage(new SendMessageRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_NamesTheInvalidAddress()
    {
        var request = new SendMessageRequest { To = ["ok@example.com"], Cc = ["not-an-address"] };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("not-an-address", ((ResultEnveloppe)bad.Value!).Message);
    }

    // Each list is capped on its own by an attribute; only the controller can count the three
    // together, and without that a single request still addresses three times the ceiling.
    [Fact]
    public async Task SendMessage_RefusesMoreRecipientsThanTheCeiling_CountedAcrossTheThreeLists()
    {
        var forty = Enumerable.Range(1, 40).Select(i => $"a{i}@weesky.be").ToList();
        var request = new SendMessageRequest { To = forty, Cc = forty, Bcc = forty };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("more than 100 recipients", ((ResultEnveloppe)bad.Value!).Message);
        _sender.Verify(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
            It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDraft_RefusesMoreRecipientsThanTheCeiling()
    {
        var forty = Enumerable.Range(1, 40).Select(i => $"a{i}@weesky.be").ToList();
        var request = new SaveDraftRequest { To = forty, Cc = forty, Bcc = forty };

        var result = await CreateController().SaveDraft(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("more than 100 recipients", ((ResultEnveloppe)bad.Value!).Message);
    }

    [Fact]
    public async Task SendMessage_RefusesAnInvalidFromAddress()
    {
        var request = new SendMessageRequest { To = ["ok@example.com"], FromAddress = "not-an-address" };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("not-an-address", ((ResultEnveloppe)bad.Value!).Message);
    }

    [Fact]
    public async Task SendMessage_NamesTheForbiddenFrom()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), Conn, It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SendMessageResult>(IMailSender.ForbiddenFrom));
        var request = new SendMessageRequest { To = ["ok@example.com"], FromAddress = "other@weesky.be" };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("other@weesky.be", ((ResultEnveloppe)bad.Value!).Message);
    }

    [Fact]
    public async Task SendMessage_PassesADecoratedFromAddressDownAsTheBareAddress()
    {
        SendMessageRequest? captured = null;
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, MailAccountConnection, SendMessageRequest, CancellationToken>((_, _, r, _) => captured = r)
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));
        var request = new SendMessageRequest
        {
            To = ["ok@example.com"], FromAddress = "\"Michel D\" <michel@weesky.be>"
        };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("michel@weesky.be", captured!.FromAddress);
    }

    [Fact]
    public async Task SendMessage_TreatsANullRecipientListAsNoRecipient()
    {
        var request = new SendMessageRequest { To = null! };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_TreatsANullReferencesListAsNoThreading()
    {
        SendMessageRequest? captured = null;
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, MailAccountConnection, SendMessageRequest, CancellationToken>((_, _, r, _) => captured = r)
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));
        var request = new SendMessageRequest { To = ["ok@example.com"], References = null! };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(captured!.References);
    }

    [Fact]
    public async Task SendMessage_RejectsANullRecipientElement()
    {
        var request = new SendMessageRequest { To = ["a@example.com", null!] };

        var result = await CreateController().SendMessage(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_AnswersUnauthorizedWithoutCredentials()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.SendMessage(
            new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_MapsUnknownAttachmentToBadRequest()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SendMessageResult>(IMailSender.UnknownAttachment));

        var result = await CreateController().SendMessage(
            new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_MapsAServerRefusalTo502()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SendMessageResult>("The mail server refused the message"));

        var result = await CreateController().SendMessage(
            new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task SendMessage_AnswersTheSendersResult()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SendMessageResult(false)));

        var result = await CreateController().SendMessage(
            new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(((SendMessageResult)ok.Value!).AppendedToSent);
    }

    /// <summary>The priority has to survive the hop from the request into the sender's argument.</summary>
    [Fact]
    public async Task SendMessage_CarriesThePriorityIntoTheOutgoingMessage()
    {
        SendMessageRequest? captured = null;
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, MailAccountConnection, SendMessageRequest, CancellationToken>((_, _, r, _) => captured = r)
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));

        var result = await CreateController().SendMessage(
            new SendMessageRequest { To = ["a@example.com"], Priority = MailPriority.High }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(MailPriority.High, captured!.Priority);
    }

    // ── PrepareQuote ────────────────────────────────────────────────────

    [Fact]
    public async Task PrepareQuote_RefusesAnUnknownPurpose()
    {
        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 1, Purpose = "resend" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrepareQuote_RefusesAMissingFolder()
    {
        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = " ", Uid = 1, Purpose = "reply" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrepareQuote_MapsMessageNotFoundTo404()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MimeMessage>(ImapSession.MessageNotFound));

        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "reply" }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrepareQuote_AnswersThePreparedQuote()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new MimeMessage()));
        var prepared = new PreparedQuote("<p>q</p>", []);
        _quotes.Setup(q => q.PrepareAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), QuotePurpose.Forward, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(prepared));

        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "forward" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(prepared, ok.Value);
    }

    [Fact]
    public async Task PrepareQuote_MapsAStagingRefusalTo400()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new MimeMessage()));
        _quotes.Setup(q => q.PrepareAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<QuotePurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PreparedQuote>("The attachment exceeds the 25 MB limit"));

        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "forward" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrepareQuote_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "reply" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrepareQuote_Returns502WhenImapFails()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MimeMessage>("Unable to read the message"));

        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "reply" }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── SaveDraft ───────────────────────────────────────────────────────

    [Fact]
    public async Task SaveDraft_Returns200WithTheSavedLocation()
    {
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), Conn, It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SavedDraft(7, "Drafts")));

        var result = await CreateController().SaveDraft(new SaveDraftRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var saved = Assert.IsType<SavedDraft>(ok.Value);
        Assert.Equal(7u, saved.Uid);
        Assert.Equal("Drafts", saved.FolderPath);
    }

    [Fact]
    public async Task SaveDraft_AcceptsNoRecipient()
    {
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), Conn, It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SavedDraft(1, "Drafts")));

        var result = await CreateController().SaveDraft(
            new SaveDraftRequest { To = [], Cc = [], Bcc = [] }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        _drafts.Verify(d => d.SaveAsync(It.IsAny<User>(), Conn, It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDraft_RejectsAMalformedRecipient()
    {
        var result = await CreateController().SaveDraft(
            new SaveDraftRequest { To = ["not an address"] }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _drafts.Verify(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDraft_RejectsAForeignFrom()
    {
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SavedDraft>(IOutgoingMessageFactory.ForbiddenFrom));

        var result = await CreateController().SaveDraft(
            new SaveDraftRequest { FromAddress = "other@weesky.be" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("other@weesky.be", ((ResultEnveloppe)bad.Value!).Message);
    }

    [Fact]
    public async Task SaveDraft_RefusesAMalformedFrom()
    {
        var result = await CreateController().SaveDraft(
            new SaveDraftRequest { FromAddress = "not an address" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _drafts.Verify(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDraft_BaresADecoratedFromBeforeTheSaver()
    {
        SaveDraftRequest? captured = null;
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, MailAccountConnection, SaveDraftRequest, CancellationToken>((_, _, r, _) => captured = r)
            .ReturnsAsync(Result.Success(new SavedDraft(1, "Drafts")));

        var result = await CreateController().SaveDraft(
            new SaveDraftRequest { FromAddress = "Name <me@weesky.be>" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("me@weesky.be", captured!.FromAddress);
    }

    [Fact]
    public async Task SaveDraft_KeepsReplaceUidThroughTheFromNormalisation()
    {
        // The normalisation rewrites the request with `with` on the base record type; the virtual
        // clone must preserve the derived SaveDraftRequest and its ReplaceUid, not slice it away.
        SaveDraftRequest? captured = null;
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, MailAccountConnection, SaveDraftRequest, CancellationToken>((_, _, r, _) => captured = r)
            .ReturnsAsync(Result.Success(new SavedDraft(42, "Drafts")));

        var result = await CreateController().SaveDraft(
            new SaveDraftRequest { FromAddress = "Name <me@weesky.be>", ReplaceUid = 41 }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(41u, captured!.ReplaceUid);
    }

    [Fact]
    public async Task SaveDraft_RejectsAnUnknownStagedId()
    {
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SavedDraft>(IOutgoingMessageFactory.UnknownAttachment));

        var result = await CreateController().SaveDraft(new SaveDraftRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SaveDraft_Returns502WithoutADraftsFolder()
    {
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SavedDraft>(IDraftSaver.NoDraftsFolder));

        var result = await CreateController().SaveDraft(new SaveDraftRequest(), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(status.Value);
        Assert.Equal(
            "This mailbox has no drafts folder. Assign the drafts role in Settings > Folders.",
            envelope.Message);
    }

    [Fact]
    public async Task SaveDraft_Returns401WithoutCredentials()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.SaveDraft(new SaveDraftRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        _drafts.Verify(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDraft_CarriesThePriorityIntoTheSavedMessage()
    {
        SaveDraftRequest? captured = null;
        _drafts.Setup(d => d.SaveAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SaveDraftRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, MailAccountConnection, SaveDraftRequest, CancellationToken>((_, _, r, _) => captured = r)
            .ReturnsAsync(Result.Success(new SavedDraft(1, "Drafts")));

        var result = await CreateController().SaveDraft(
            new SaveDraftRequest { Priority = MailPriority.Low }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(MailPriority.Low, captured!.Priority);
    }

    // ── OpenDraft ───────────────────────────────────────────────────────

    [Fact]
    public async Task OpenDraft_ReturnsTheEnvelopeAndPreparedBody()
    {
        var message = new MimeMessage();
        message.To.Add(MailboxAddress.Parse("a@example.com"));
        message.To.Add(MailboxAddress.Parse("b@example.com"));
        message.Cc.Add(MailboxAddress.Parse("c@example.com"));
        message.Subject = "Draft subject";
        message.From.Add(MailboxAddress.Parse("Me <me@weesky.be>"));
        message.InReplyTo = MimeUtils.ParseMessageId("<parent@x.com>");
        message.References.Add(MimeUtils.ParseMessageId("<oldest@x.com>")!);
        message.References.Add(MimeUtils.ParseMessageId("<newest@x.com>")!);
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), Conn, "Drafts", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(message));
        var stagedInfo = new StagedAttachmentInfo(Guid.NewGuid(), "logo.png", 3, "image/png", "logo@mail");
        var prepared = new PreparedQuote("<p>Hi</p>", [stagedInfo]);
        _quotes.Setup(q => q.PrepareAsync(StagedScope, message, QuotePurpose.EditAsNew, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(prepared));

        var result = await CreateController().OpenDraft(
            new OpenDraftRequest("Drafts", 7), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var opened = Assert.IsType<OpenedDraft>(ok.Value);
        Assert.Equal(["a@example.com", "b@example.com"], opened.To);
        Assert.Equal(["c@example.com"], opened.Cc);
        Assert.Empty(opened.Bcc);
        Assert.Equal("Draft subject", opened.Subject);
        Assert.Equal("me@weesky.be", opened.FromAddress);
        Assert.Equal("<p>Hi</p>", opened.HtmlBody);
        Assert.Same(stagedInfo, Assert.Single(opened.Attachments));
        Assert.Equal("parent@x.com", opened.InReplyTo);
        Assert.Equal(["oldest@x.com", "newest@x.com"], opened.References);
    }

    /// <summary>Saved at High, reopened at High — otherwise the setting dies on the round trip.</summary>
    [Fact]
    public async Task OpenDraft_ReadsThePriorityBackOffTheSavedMessage()
    {
        var saved = new MimeMessage();
        MailPriorityHeaders.Apply(saved, MailPriority.High);
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), Conn, "Drafts", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(saved));
        _quotes.Setup(q => q.PrepareAsync(StagedScope, saved, QuotePurpose.EditAsNew, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PreparedQuote("<p>Hi</p>", [])));

        var result = await CreateController().OpenDraft(
            new OpenDraftRequest("Drafts", 7), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(MailPriority.High, Assert.IsType<OpenedDraft>(ok.Value).Priority);
    }

    /// <summary>A draft written as text reopens as text; without this it comes back as HTML.</summary>
    [Fact]
    public async Task OpenDraft_ReportsATextOnlyDraftAsPlainText()
    {
        var saved = new MimeMessage { Body = new TextPart("plain") { Text = "hello\nthere" } };
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), Conn, "Drafts", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(saved));
        _quotes.Setup(q => q.PrepareAsync(StagedScope, saved, QuotePurpose.EditAsNew, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PreparedQuote("<div>hello<br>there</div>", [])));

        var result = await CreateController().OpenDraft(
            new OpenDraftRequest("Drafts", 7), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("hello\nthere", Assert.IsType<OpenedDraft>(ok.Value).TextBody);
    }

    [Fact]
    public async Task OpenDraft_LeavesAnHtmlDraftWithNoTextBody()
    {
        var saved = new MimeMessage { Body = new TextPart("html") { Text = "<p>Hi</p>" } };
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), Conn, "Drafts", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(saved));
        _quotes.Setup(q => q.PrepareAsync(StagedScope, saved, QuotePurpose.EditAsNew, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PreparedQuote("<p>Hi</p>", [])));

        var result = await CreateController().OpenDraft(
            new OpenDraftRequest("Drafts", 7), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Null(Assert.IsType<OpenedDraft>(ok.Value).TextBody);
    }

    [Fact]
    public async Task OpenDraft_Returns404ForAMissingUid()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "Drafts", 9u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MimeMessage>(ImapSession.MessageNotFound));

        var result = await CreateController().OpenDraft(
            new OpenDraftRequest("Drafts", 9), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task OpenDraft_Returns400WhenStagingFails()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "Drafts", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new MimeMessage()));
        _quotes.Setup(q => q.PrepareAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), QuotePurpose.EditAsNew, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PreparedQuote>("cap"));

        var result = await CreateController().OpenDraft(
            new OpenDraftRequest("Drafts", 7), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OpenDraft_RequiresAFolder()
    {
        var result = await CreateController().OpenDraft(
            new OpenDraftRequest("", 7), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _messages.Verify(m => m.GetMimeMessageAsync(
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
