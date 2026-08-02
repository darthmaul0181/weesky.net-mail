using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class MailMessagesControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "hunter2");

    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();
    private readonly Mock<ITrustedSenderStore> _trustedSenders = new();

    private MailMessagesController CreateController()
    {
        ResolveTo(Conn);

        return new MailMessagesController(_messages.Object, _connections.Object, _trustedSenders.Object,
                                          NullLogger<MailMessagesController>.Instance)
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

    // ── Messages ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessages_ReturnsThePage()
    {
        _messages.Setup(m => m.ListAsync(It.IsAny<User>(), Conn, "INBOX", 0, 50, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new MailFolderPage
                 {
                     FolderPath = "INBOX",
                     UidValidity = 42,
                     Total = 1,
                     Messages = { new MailMessageSummary { Uid = 7, Subject = "Hello" } }
                 }));

        var result = await CreateController().GetMessages("INBOX", 0, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<MailFolderPage>(ok.Value);
        Assert.Equal(42u, page.UidValidity);
        Assert.Single(page.Messages);
    }

    // A folder deleted from another client is an ordinary race, not a mail-server refusal.
    [Fact]
    public async Task GetMessages_Returns404WhenTheFolderIsGone()
    {
        _messages.Setup(m => m.ListAsync(It.IsAny<User>(), Conn, "Gone", 0, 50, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailFolderPage>(ImapSession.FolderNotFound));

        var result = await CreateController().GetMessages("Gone", 0, 50, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task SearchMessages_Returns404WhenTheFolderIsGone()
    {
        _messages.Setup(m => m.SearchAsync(
                It.IsAny<User>(), Conn, "Gone", false,
                It.IsAny<MailSearchCriteria>(), 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailSearchPage>(ImapSession.FolderNotFound));

        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "Gone", Quick = "x", Page = 0, PageSize = 50 },
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessages_Returns400ForABlankFolder()
    {
        var result = await CreateController().GetMessages("  ", 0, 50, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        VerifyMessagesNeverCalled();
    }

    [Fact]
    public async Task GetMessages_Returns400ForANegativePage()
    {
        var result = await CreateController().GetMessages("INBOX", -1, 50, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        VerifyMessagesNeverCalled();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task GetMessages_Returns400ForAPageSizeOutOfRange(int pageSize)
    {
        var result = await CreateController().GetMessages("INBOX", 0, pageSize, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        VerifyMessagesNeverCalled();
    }

    [Fact]
    public async Task GetMessages_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.GetMessages("INBOX", 0, 50, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessages_Returns502WhenImapFails()
    {
        _messages.Setup(m => m.ListAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailFolderPage>("Unable to read the messages"));

        var result = await CreateController().GetMessages("INBOX", 0, 50, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    private void VerifyMessagesNeverCalled()
        => _messages.Verify(m => m.ListAsync(
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // ── Message detail ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMessage_ReturnsTheDetail()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), Conn, "INBOX", 42u, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new MailMessageDetail { Uid = 42, Subject = "Re: facture" }));

        var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Re: facture", Assert.IsType<MailMessageDetail>(ok.Value).Subject);
    }

    [Fact]
    public async Task GetMessage_Returns404WhenTheUidDoesNotResolve()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound));

        var result = await CreateController().GetMessage("INBOX", 999, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessage_Returns502ForAnyOtherFailure()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageDetail>("Unable to read the message"));

        var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task GetMessage_Returns400ForABlankFolder()
    {
        var result = await CreateController().GetMessage("", 42, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessage_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.GetMessage("INBOX", 42, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    // ── Message source ──────────────────────────────────────────────────

    private static MailMessageSource Source(long totalBytes, bool truncated) => new(
        Subject: "Mount ZFS on rescue system",
        MessageId: "c24494a9de@weesky.be",
        Date: new DateTimeOffset(2026, 2, 2, 1, 1, 0, TimeSpan.Zero),
        FromName: "Michaël",
        FromAddress: "darth@weesky.be",
        To: new List<MailAddressInfo> { new("", "darthmaul0181@gmail.com") },
        Authentication: new MailAuthentication("pass", "pass", "pass", "mx.google.com; spf=pass"),
        Source: "Delivered-To: darthmaul0181@gmail.com\r\n",
        TotalBytes: totalBytes,
        Truncated: truncated);

    [Fact]
    public async Task GetMessageSource_ReturnsTheSource()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), Conn, "INBOX", 42u, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(Source(1024, false)));

        var result = await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<MailMessageSource>(ok.Value);
        Assert.Equal("Mount ZFS on rescue system", payload.Subject);
        Assert.Equal("pass", payload.Authentication!.Dmarc);
        Assert.False(payload.Truncated);
    }

    [Fact]
    public async Task GetMessageSource_AsksForOneMegabyte()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), Conn, "INBOX", 42u, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(Source(1024, false)));

        await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        _messages.Verify(m => m.GetSourceAsync(
            It.IsAny<User>(), Conn, "INBOX", 42u, 1024 * 1024, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMessageSource_Returns400ForABlankFolder()
    {
        var result = await CreateController().GetMessageSource("  ", 42, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessageSource_Returns404WhenTheMessageIsGone()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(),
                     It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageSource>(ImapSession.MessageNotFound));

        var result = await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessageSource_Returns502WhenImapFails()
    {
        _messages.Setup(m => m.GetSourceAsync(
                     It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(),
                     It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageSource>("Unable to read the message source"));

        var result = await CreateController().GetMessageSource("INBOX", 42, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── Attachment ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAttachment_ReturnsTheFileWithAnAttachmentDisposition()
    {
        _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), Conn, "INBOX", 42u, "2", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new MailAttachmentContent
                 {
                     Content = new MemoryStream([1, 2, 3]),
                     FileName = "report.pdf",
                     ContentType = "application/pdf"
                 }));

        var result = await CreateController().GetAttachment("INBOX", 42, "2", CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("report.pdf", file.FileDownloadName);

        using var read = new MemoryStream();
        file.FileStream.CopyTo(read);
        Assert.Equal(new byte[] { 1, 2, 3 }, read.ToArray());
    }

    // A message whose whole body is the attachment has no multipart wrapper to number, so its
    // specifier is empty — refusing it as blank made every such attachment undownloadable.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetAttachment_ServesTheRootPartForAnEmptySpecifier(string? part)
    {
        _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), Conn, "INBOX", 42u, "", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new MailAttachmentContent
                 {
                     Content = new MemoryStream([1]),
                     FileName = "report.zip",
                     ContentType = "application/zip"
                 }));

        var result = await CreateController().GetAttachment("INBOX", 42, part, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("report.zip", file.FileDownloadName);
    }

    [Fact]
    public async Task GetAttachment_Returns400ForABlankFolder()
    {
        var result = await CreateController().GetAttachment("", 42, "2", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAttachment_Returns404WhenThePartDoesNotResolve()
    {
        _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound));

        var result = await CreateController().GetAttachment("INBOX", 42, "99", CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, status.StatusCode);
    }

    [Fact]
    public async Task GetAttachment_Returns502ForAnyOtherFailure()
    {
        _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailAttachmentContent>("Unable to read the attachment"));

        var result = await CreateController().GetAttachment("INBOX", 42, "2", CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── Flags ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetMessageFlags_Returns204AndDelegates()
    {
        _messages.Setup(m => m.SetFlagsAsync(It.IsAny<User>(), Conn, "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), MailFlag.Seen, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [42u], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.SetFlagsAsync(It.IsAny<User>(), Conn, "INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 42 })),
            MailFlag.Seen, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // An initialiser only covers an *absent* property. A body carrying an explicit "uids": null
    // overwrites it, and the count check then dereferenced null: a 500 on a malformed request.
    [Theory]
    [InlineData("flags")]
    [InlineData("move")]
    [InlineData("copy")]
    [InlineData("delete")]
    public async Task MessageBatch_WithExplicitlyNullUids_Returns400NotAnUnhandledException(string verb)
    {
        var controller = CreateController();

        ActionResult result = verb switch
        {
            "flags" => await controller.SetMessageFlags(
                new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = null!, Flag = MailFlag.Seen }, CancellationToken.None),
            "move" => await controller.MoveMessages(
                new MoveMessagesRequest { FolderPath = "INBOX", Uids = null!, TargetFolderPath = "Trash" }, CancellationToken.None),
            "copy" => await controller.CopyMessages(
                new MoveMessagesRequest { FolderPath = "INBOX", Uids = null!, TargetFolderPath = "Trash" }, CancellationToken.None),
            _ => await controller.DeleteMessages(
                new DeleteMessagesRequest { FolderPath = "INBOX", Uids = null! }, CancellationToken.None),
        };

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetMessageFlags_Returns400WithoutAFolder()
    {
        var result = await CreateController().SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = " ", Uids = [1u], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetMessageFlags_Returns400OnAnEmptyBatch()
    {
        var result = await CreateController().SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetMessageFlags_Returns400Above200Uids()
    {
        var uids = Enumerable.Range(1, 201).Select(i => (uint)i).ToList();

        var result = await CreateController().SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = uids, Flag = MailFlag.Flagged, Value = true },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetMessageFlags_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [1u], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task SetMessageFlags_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.SetFlagsAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<MailFlag>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to update the messages"));

        var result = await CreateController().SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [1u], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── Move / Copy ─────────────────────────────────────────────────────

    [Fact]
    public async Task Move_Returns204AndDelegates()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), Conn, "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), "Archive", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [42u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.MoveOrCopyAsync(It.IsAny<User>(), Conn, "INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 42 })),
            "Archive", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Move_Returns400WithoutASource()
    {
        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = " ", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A folder is required", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Move_Returns400WithoutATarget()
    {
        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = " " },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A target folder is required", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Move_Returns400OnAnEmptyBatch()
    {
        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Move_Returns400Above200Uids()
    {
        var uids = Enumerable.Range(1, 201).Select(i => (uint)i).ToList();

        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = uids, TargetFolderPath = "Archive" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    // Pins the order between the uid-count guard and the target checks: with both
    // simultaneously violated, the uid-count message must win.
    [Fact]
    public async Task Move_Returns400ForUidCountEvenWhenTargetEqualsSource()
    {
        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [], TargetFolderPath = "INBOX" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Move_Returns400WhenTargetEqualsSource()
    {
        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "INBOX" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("The target folder must differ from the source folder", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Move_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Move_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to move the messages"));

        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task Move_Returns400WhenTheTargetIsNotSelectable()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ImapSession.TargetNotSelectable));

        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Notes" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var envelope = Assert.IsType<ResultEnveloppe>(bad.Value);
        Assert.Equal("The target folder cannot hold messages", envelope.Message);
    }

    [Fact]
    public async Task Copy_Returns204AndDelegates()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), Conn, "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), "Archive", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [42u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.MoveOrCopyAsync(It.IsAny<User>(), Conn, "INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 42 })),
            "Archive", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Copy_Returns400WithoutASource()
    {
        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = " ", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A folder is required", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Copy_Returns400WithoutATarget()
    {
        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = " " },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A target folder is required", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Copy_Returns400OnAnEmptyBatch()
    {
        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Copy_Returns400Above200Uids()
    {
        var uids = Enumerable.Range(1, 201).Select(i => (uint)i).ToList();

        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = uids, TargetFolderPath = "Archive" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    // Pins the order between the uid-count guard and the target checks: with both
    // simultaneously violated, the uid-count message must win.
    [Fact]
    public async Task Copy_Returns400ForUidCountEvenWhenTargetEqualsSource()
    {
        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [], TargetFolderPath = "INBOX" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Copy_Returns400WhenTargetEqualsSource()
    {
        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "INBOX" },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("The target folder must differ from the source folder", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Copy_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Copy_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to copy the messages"));

        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Returns204AndDelegates()
    {
        _messages.Setup(m => m.DeleteAsync(It.IsAny<User>(), Conn, "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = [42u] },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.DeleteAsync(It.IsAny<User>(), Conn, "INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 42 })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Returns400WithoutASource()
    {
        var result = await CreateController().DeleteMessages(
            new DeleteMessagesRequest { FolderPath = " ", Uids = [1u] },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A folder is required", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Delete_Returns400OnAnEmptyBatch()
    {
        var result = await CreateController().DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = [] },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Delete_Returns400Above200Uids()
    {
        var uids = Enumerable.Range(1, 201).Select(i => (uint)i).ToList();

        var result = await CreateController().DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = uids },
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Uids must hold between 1 and 200 entries", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Delete_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = [1u] },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Delete_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.DeleteAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to delete the messages"));

        var result = await CreateController().DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = [1u] },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns502WithTheUidplusMessage()
    {
        _messages.Setup(m => m.DeleteAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("The mail server cannot delete single messages (no UIDPLUS)"));

        var result = await CreateController().DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = [1u] },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(status.Value);
        Assert.Equal("The mail server cannot delete single messages (no UIDPLUS)", envelope.Message);
    }

    // ── Search ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchMessages_requires_a_folder()
    {
        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "", Quick = "x" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchMessages_refuses_a_negative_page()
    {
        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", Quick = "x", Page = -1 }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task SearchMessages_bounds_the_page_size(int pageSize)
    {
        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", Quick = "x", PageSize = pageSize }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchMessages_requires_at_least_one_criterion()
    {
        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchMessages_answers_401_without_credentials()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", Quick = "x" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchMessages_returns_the_page()
    {
        _messages.Setup(m => m.SearchAsync(
                It.IsAny<User>(), Conn, "INBOX", true,
                It.Is<MailSearchCriteria>(c => c.Quick == "hello"), 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new MailSearchPage { Total = 2 }));

        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", AllFolders = true, Quick = "hello", Page = 2, PageSize = 25 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<MailSearchPage>(ok.Value);
        Assert.Equal(2, page.Total);
        _messages.Verify(m => m.SearchAsync(
            It.IsAny<User>(), Conn, "INBOX", true,
            It.Is<MailSearchCriteria>(c => c.Quick == "hello"), 2, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMessages_maps_server_failure_to_502()
    {
        _messages.Setup(m => m.SearchAsync(
                It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<MailSearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailSearchPage>("boom"));

        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", Quick = "x" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    // ── Trusted-sender bookkeeping ──────────────────────────────────────

    // The reader is already fetching this message; a dedicated client call would buy a second
    // round trip per open for nothing.
    [Fact]
    public async Task GetMessage_RecordsTheSenderUse()
    {
        var detail = new MailMessageDetail { FromAddress = "news@example.com" };
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 42u,
                                        It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(detail));

        await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        _trustedSenders.Verify(
            s => s.TouchAsync(It.IsAny<Guid>(), "news@example.com", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Rule 5: IMAP first, bookkeeping second. A failed write degrades, it never fails the read
    // the caller actually asked for.
    [Fact]
    public async Task GetMessage_WhenRecordingTheUseThrows_StillReturnsTheMessage()
    {
        var detail = new MailMessageDetail { FromAddress = "news@example.com" };
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 42u,
                                        It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(detail));
        _trustedSenders.Setup(s => s.TouchAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                                                It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new InvalidOperationException("database is away"));

        var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(detail, ok.Value);
    }

    // Switching messages quickly aborts the detail request: a routine disconnect, not a fault.
    [Fact]
    public async Task GetMessage_WhenRecordingTheUseIsCancelled_StillReturnsTheMessage()
    {
        var detail = new MailMessageDetail { FromAddress = "news@example.com" };
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 42u,
                                        It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(detail));
        _trustedSenders.Setup(s => s.TouchAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                                                It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new OperationCanceledException());

        var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(detail, ok.Value);
    }

    [Fact]
    public async Task GetMessage_WhenTheReadFails_RecordsNothing()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "INBOX", 42u,
                                        It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound));

        await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        _trustedSenders.Verify(
            s => s.TouchAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
