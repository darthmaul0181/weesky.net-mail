using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class MailControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();

    private readonly Mock<IMailFolderRepository> _folders = new();
    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<IMailCredentialStore> _credentials = new();
    private readonly Mock<IFolderRoleStore> _roleStore = new();
    private readonly Mock<IStagedAttachmentStore> _staged = new();
    private readonly Mock<IMailSender> _sender = new();
    private readonly Mock<IQuotePreparer> _quotes = new();

    private MailController CreateController()
    {
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));
        _roleStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<FolderRoleOverride>());

        return new MailController(_folders.Object, _messages.Object, _credentials.Object, _roleStore.Object,
                                  _staged.Object, _sender.Object, _quotes.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", WebmailUid)
        };
    }

    private void SetupTree(params MailFolderNode[] nodes)
        => _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(nodes.ToList()));

    [Fact]
    public async Task GetFolders_ReturnsTheTree()
    {
        SetupTree(new MailFolderNode { Path = "INBOX", Name = "INBOX", SpecialUse = "inbox" });

        var result = await CreateController().GetFolders(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var tree = Assert.IsAssignableFrom<IReadOnlyList<MailFolderNode>>(ok.Value);
        Assert.Single(tree);
    }

    [Fact]
    public async Task GetFolders_PassesTheDecryptedPasswordToTheRepository()
    {
        SetupTree();

        await CreateController().GetFolders(CancellationToken.None);

        _folders.Verify(f => f.GetTreeAsync(It.IsAny<User>(), "hunter2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFolders_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.GetFolders(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ResultEnveloppe>(unauthorized.Value);
        Assert.Equal("credentials_unavailable", envelope.Message);
    }

    [Fact]
    public async Task GetFolders_DoesNotReachTheRepositoryWithoutCredentials()
    {
        var controller = CreateController();
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        await controller.GetFolders(CancellationToken.None);

        _folders.Verify(
            f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetFolders_Returns502WhenImapFails()
    {
        _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<IReadOnlyList<MailFolderNode>>("Unable to connect to the mail service"));

        var result = await CreateController().GetFolders(CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task GetFolders_NeverLeaksTheCredentialsIntoTheResponse()
    {
        SetupTree(new MailFolderNode { Path = "INBOX", Name = "INBOX" });

        var result = await CreateController().GetFolders(CancellationToken.None);

        var payload = System.Text.Json.JsonSerializer.Serialize(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.DoesNotContain("hunter2", payload);
    }

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateFolder_ReturnsTheNewPath()
    {
        _folders.Setup(f => f.CreateFolderAsync(It.IsAny<User>(), "hunter2", "INBOX", "Projects", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success("INBOX/Projects"));

        var result = await CreateController().CreateFolder(
            new CreateFolderRequest { ParentPath = "INBOX", Name = "Projects" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("INBOX/Projects", ok.Value);
    }

    [Fact]
    public async Task CreateFolder_Returns400ForABlankNameWithoutReachingTheRepository()
    {
        var result = await CreateController().CreateFolder(
            new CreateFolderRequest { ParentPath = "", Name = "   " }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _folders.Verify(f => f.CreateFolderAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateFolder_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.CreateFolder(
            new CreateFolderRequest { Name = "Projects" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateFolder_Returns502WhenImapFails()
    {
        _folders.Setup(f => f.CreateFolderAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<string>("Unable to create the folder"));

        var result = await CreateController().CreateFolder(
            new CreateFolderRequest { Name = "Projects" }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── Rename ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameFolder_ReturnsThePath()
    {
        SetupTree(RoleNode("Old"));
        _folders.Setup(f => f.RenameFolderAsync(It.IsAny<User>(), "hunter2", "Old", "INBOX", "New", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success("INBOX/New"));

        var result = await CreateController().RenameFolder(
            new RenameFolderRequest { Path = "Old", NewParentPath = "INBOX", NewName = "New" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("INBOX/New", ok.Value);
    }

    [Fact]
    public async Task RenameFolder_Returns400WhenPathOrNameIsMissing()
    {
        var controller = CreateController();

        Assert.IsType<BadRequestObjectResult>(
            (await controller.RenameFolder(new RenameFolderRequest { Path = "", NewName = "New" }, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await controller.RenameFolder(new RenameFolderRequest { Path = "Old", NewName = " " }, CancellationToken.None)).Result);

        _folders.Verify(f => f.RenameFolderAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteFolder_Returns204OnSuccess()
    {
        SetupTree(RoleNode("Projects"));
        _folders.Setup(f => f.DeleteFolderAsync(It.IsAny<User>(), "hunter2", "Projects", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

        var result = await CreateController().DeleteFolder(
            new DeleteFolderRequest { Path = "Projects" }, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_Returns400ForABlankPathWithoutReachingTheRepository()
    {
        var result = await CreateController().DeleteFolder(
            new DeleteFolderRequest { Path = "" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _folders.Verify(f => f.DeleteFolderAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteFolder_Returns502WhenTheServerRefuses()
    {
        SetupTree(RoleNode("Projects"));
        _folders.Setup(f => f.DeleteFolderAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure("The mail server refused the operation"));

        var result = await CreateController().DeleteFolder(
            new DeleteFolderRequest { Path = "Projects" }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── Subscription ────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetFolderSubscription_Returns204AndPassesTheState(bool subscribed)
    {
        SetupTree(RoleNode("Projects"));
        _folders.Setup(f => f.SetSubscriptionAsync(It.IsAny<User>(), "hunter2", "Projects", subscribed, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

        var result = await CreateController().SetFolderSubscription(
            new FolderSubscriptionRequest { Path = "Projects", Subscribed = subscribed }, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _folders.Verify(f => f.SetSubscriptionAsync(
            It.IsAny<User>(), "hunter2", "Projects", subscribed, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFolderSubscription_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.SetFolderSubscription(
            new FolderSubscriptionRequest { Path = "Projects", Subscribed = true }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    // ── Messages ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessages_ReturnsThePage()
    {
        _messages.Setup(m => m.ListAsync(It.IsAny<User>(), "hunter2", "INBOX", 0, 50, It.IsAny<CancellationToken>()))
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.GetMessages("INBOX", 0, 50, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessages_Returns502WhenImapFails()
    {
        _messages.Setup(m => m.ListAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailFolderPage>("Unable to read the messages"));

        var result = await CreateController().GetMessages("INBOX", 0, 50, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    private void VerifyMessagesNeverCalled()
        => _messages.Verify(m => m.ListAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // ── Message detail ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMessage_ReturnsTheDetail()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), "hunter2", "INBOX", 42u, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new MailMessageDetail { Uid = 42, Subject = "Re: facture" }));

        var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Re: facture", Assert.IsType<MailMessageDetail>(ok.Value).Subject);
    }

    [Fact]
    public async Task GetMessage_Returns404WhenTheUidDoesNotResolve()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound));

        var result = await CreateController().GetMessage("INBOX", 999, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetMessage_Returns502ForAnyOtherFailure()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.GetMessage("INBOX", 42, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    // ── Attachment ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAttachment_ReturnsTheFileWithAnAttachmentDisposition()
    {
        _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), "hunter2", "INBOX", 42u, "2", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new MailAttachmentContent
                 {
                     Content = new byte[] { 1, 2, 3 },
                     FileName = "report.pdf",
                     ContentType = "application/pdf"
                 }));

        var result = await CreateController().GetAttachment("INBOX", 42, "2", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("report.pdf", file.FileDownloadName);
        Assert.Equal(new byte[] { 1, 2, 3 }, file.FileContents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAttachment_Returns400ForABlankPart(string part)
    {
        var result = await CreateController().GetAttachment("INBOX", 42, part, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAttachment_Returns404WhenThePartDoesNotResolve()
    {
        _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound));

        var result = await CreateController().GetAttachment("INBOX", 42, "99", CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, status.StatusCode);
    }

    [Fact]
    public async Task GetAttachment_Returns502ForAnyOtherFailure()
    {
        _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailAttachmentContent>("Unable to read the attachment"));

        var result = await CreateController().GetAttachment("INBOX", 42, "2", CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // ── System folders are locked against rename, delete and hide ───────
    // The client disables these controls too, but a guard living in one client is one new
    // screen away from being forgotten.

    [Fact]
    public async Task RenameFolder_RefusesAFolderHoldingARole()
    {
        SetupTree(RoleNode("Corbeille", attributeRole: "trash"));

        var result = await CreateController().RenameFolder(
            new RenameFolderRequest { Path = "Corbeille", NewName = "Poubelle" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        // The message must name the role, or the user cannot tell what to change.
        Assert.Contains("trash", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _folders.Verify(f => f.RenameFolderAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteFolder_RefusesAFolderHoldingARole()
    {
        SetupTree(RoleNode("Sent", attributeRole: "sent"));

        var result = await CreateController().DeleteFolder(
            new DeleteFolderRequest { Path = "Sent" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _folders.Verify(f => f.DeleteFolderAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Deleting a parent takes its children with it.
    [Fact]
    public async Task DeleteFolder_RefusesAFolderWhoseChildHoldsARole()
    {
        var parent = RoleNode("Mail");
        parent.Children = [RoleNode("Mail/Trash", attributeRole: "trash")];
        SetupTree(parent);

        var result = await CreateController().DeleteFolder(
            new DeleteFolderRequest { Path = "Mail" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _folders.Verify(f => f.DeleteFolderAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteFolder_RefusesTheInbox()
    {
        SetupTree(RoleNode("INBOX", attributeRole: "inbox"));

        var result = await CreateController().DeleteFolder(
            new DeleteFolderRequest { Path = "INBOX" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _folders.Verify(f => f.DeleteFolderAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The guard reads the resolution chain, not the SPECIAL-USE flags alone.
    [Fact]
    public async Task RenameFolder_RefusesAFolderHoldingARoleByOverride()
    {
        SetupTree(RoleNode("Bin", uidValidity: 42));
        // CreateController first: Moq's last matching setup wins, so its catch-all GetAsync
        // would shadow this one.
        var controller = CreateController();
        SetupOverrides(new FolderRoleOverride
        {
            UserId = WebmailUid, Role = "trash", FolderPath = "Bin", UidValidity = 42
        });

        var result = await controller.RenameFolder(
            new RenameFolderRequest { Path = "Bin", NewName = "Other" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetFolderSubscription_RefusesToHideAFolderHoldingARole()
    {
        SetupTree(RoleNode("Corbeille", attributeRole: "trash"));

        var result = await CreateController().SetFolderSubscription(
            new FolderSubscriptionRequest { Path = "Corbeille", Subscribed = false }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _folders.Verify(f => f.SetSubscriptionAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Only hiding is refused.
    [Fact]
    public async Task SetFolderSubscription_AllowsShowingAFolderHoldingARole()
    {
        SetupTree(RoleNode("Corbeille", attributeRole: "trash"));
        _folders.Setup(f => f.SetSubscriptionAsync(It.IsAny<User>(), It.IsAny<string>(), "Corbeille", true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

        var result = await CreateController().SetFolderSubscription(
            new FolderSubscriptionRequest { Path = "Corbeille", Subscribed = true }, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
    }

    // ── Folder roles ────────────────────────────────────────────────────

    private static MailFolderNode RoleNode(string path, string? attributeRole = null, uint uidValidity = 1) =>
        new() { Path = path, Name = path, AttributeRole = attributeRole, UidValidity = uidValidity };

    private void SetupOverrides(params FolderRoleOverride[] rows)
        => _roleStore.Setup(s => s.GetAsync(WebmailUid, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(rows.ToList());

    private void SetupStatus(string path, uint uidValidity = 1, string? mailboxId = null, bool selectable = true)
        => _folders.Setup(f => f.GetFolderStatusAsync(It.IsAny<User>(), It.IsAny<string>(), path, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(new MailFolderStatus
                   { Path = path, UidValidity = uidValidity, MailboxId = mailboxId, Selectable = selectable }));

    // GET /Folders now returns the chain's output, not raw discovery: the overridden
    // folder carries the overridden role, and the flagged one loses it.
    [Fact]
    public async Task GetFolders_StampsTheResolvedRolesOntoTheTree()
    {
        // CreateController() must run first: it installs a catch-all GetAsync stub, and
        // Moq resolves overlapping setups by recency, not specificity — so the
        // account-specific override below has to be configured after it to take effect.
        var controller = CreateController();
        SetupTree(RoleNode("Deleted Items", attributeRole: "trash"), RoleNode("Corbeille"));
        SetupOverrides(new FolderRoleOverride
        { UserId = WebmailUid, Role = "trash", FolderPath = "Corbeille", UidValidity = 1 });

        var result = await controller.GetFolders(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var tree = Assert.IsAssignableFrom<IReadOnlyList<MailFolderNode>>(ok.Value);
        Assert.Equal("trash", tree.Single(n => n.Path == "Corbeille").SpecialUse);
        Assert.Null(tree.Single(n => n.Path == "Deleted Items").SpecialUse);
    }

    [Fact]
    public async Task GetFolderRoles_ReturnsTheFiveRolesWithProvenance()
    {
        SetupTree(RoleNode("INBOX", attributeRole: "inbox"), RoleNode("Sent", attributeRole: "sent"));

        var result = await CreateController().GetFolderRoles(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<IReadOnlyList<FolderRoleEntry>>(ok.Value);
        Assert.Equal(5, roles.Count);
        Assert.Equal("specialUse", roles.Single(r => r.Role == "sent").Provenance);
        Assert.Null(roles.Single(r => r.Role == "archive").FolderPath);
    }

    [Fact]
    public async Task SetFolderRole_RejectsAMissingBody()
    {
        var result = await CreateController().SetFolderRole(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData("corbeille")]
    [InlineData("")]
    public async Task SetFolderRole_RejectsAnUnknownRole(string role)
    {
        var result = await CreateController().SetFolderRole(
            new SetFolderRoleRequest { Role = role, FolderPath = "X" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetFolderRole_RejectsTheInboxAsTarget()
    {
        var result = await CreateController().SetFolderRole(
            new SetFolderRoleRequest { Role = "trash", FolderPath = "INBOX" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // The client's tree can be stale — another client may have deleted the folder. The
    // PUT validates against the live mailbox, never against what the client displayed.
    [Fact]
    public async Task SetFolderRole_Returns404WhenTheFolderIsGone()
    {
        _folders.Setup(f => f.GetFolderStatusAsync(It.IsAny<User>(), It.IsAny<string>(), "Gone", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<MailFolderStatus>(ImapSession.FolderNotFound));

        var result = await CreateController().SetFolderRole(
            new SetFolderRoleRequest { Role = "trash", FolderPath = "Gone" }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // A trash that cannot hold messages is not a trash — and 2b would fail writing to it.
    [Fact]
    public async Task SetFolderRole_RejectsANonSelectableFolder()
    {
        SetupStatus("Container", selectable: false);

        var result = await CreateController().SetFolderRole(
            new SetFolderRoleRequest { Role = "trash", FolderPath = "Container" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetFolderRole_RejectsAFolderAlreadyHoldingAnotherRole()
    {
        // See the comment in GetFolders_StampsTheResolvedRolesOntoTheTree: CreateController()
        // must run before the account-specific override is configured.
        var controller = CreateController();
        SetupStatus("X");
        SetupTree(RoleNode("X"));
        SetupOverrides(new FolderRoleOverride
        { UserId = WebmailUid, Role = "junk", FolderPath = "X", UidValidity = 1 });

        var result = await controller.SetFolderRole(
            new SetFolderRoleRequest { Role = "trash", FolderPath = "X" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        // The refusal has to name the role that holds the folder, and the way out of it.
        var envelope = Assert.IsType<ResultEnveloppe>(bad.Value);
        Assert.Contains("junk", envelope.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automatic", envelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    // The API rejected a folder its own Settings page offered. The page derives its picker
    // from the resolver's output, where a stale row holds nothing; the guard read the raw
    // rows, where it still does. Same data, two readings — this is the row they disagree on.
    [Fact]
    public async Task SetFolderRole_AcceptsAFolderWhoseOtherRoleIsOnlyHeldByAStaleRow()
    {
        var controller = CreateController();
        SetupStatus("Corbeille", uidValidity: 42);
        // The live folder's UIDVALIDITY moved on: deleted and recreated outside this app,
        // so the stored trash row is stale and Corbeille is free again.
        SetupTree(RoleNode("Corbeille", uidValidity: 42));
        SetupOverrides(new FolderRoleOverride
        { UserId = WebmailUid, Role = "trash", FolderPath = "Corbeille", UidValidity = 1 });

        var result = await controller.SetFolderRole(
            new SetFolderRoleRequest { Role = "junk", FolderPath = "Corbeille" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _roleStore.Verify(s => s.UpsertAsync(It.Is<FolderRoleOverride>(o =>
            o.Role == "junk" && o.FolderPath == "Corbeille"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFolderRole_Returns502WhenTheTreeCannotBeRead()
    {
        var controller = CreateController();
        SetupStatus("X");
        _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<IReadOnlyList<MailFolderNode>>("Unable to read the mailbox folders"));

        var result = await controller.SetFolderRole(
            new SetFolderRoleRequest { Role = "trash", FolderPath = "X" }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    // uid_validity and mailbox_id come from the live folder, captured server-side — the
    // client never supplies them.
    [Fact]
    public async Task SetFolderRole_StoresTheLiveIdentityUnderTheWebmailUid()
    {
        SetupStatus("Corbeille", uidValidity: 77, mailboxId: "M1");
        SetupTree(RoleNode("Corbeille", uidValidity: 77));

        var result = await CreateController().SetFolderRole(
            new SetFolderRoleRequest { Role = "trash", FolderPath = "Corbeille" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _roleStore.Verify(s => s.UpsertAsync(It.Is<FolderRoleOverride>(o =>
            o.UserId == WebmailUid && o.Role == "trash" && o.FolderPath == "Corbeille"
            && o.UidValidity == 77UL && o.MailboxId == "M1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearFolderRole_RejectsAnUnknownRole()
    {
        var result = await CreateController().ClearFolderRole("poubelle", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ClearFolderRole_DeletesAndReturns204()
    {
        var result = await CreateController().ClearFolderRole("trash", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _roleStore.Verify(s => s.DeleteAsync(WebmailUid, "trash", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetMessageFlags_Returns204AndDelegates()
    {
        _messages.Setup(m => m.SetFlagsAsync(It.IsAny<User>(), "hunter2", "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), MailFlag.Seen, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [42u], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.SetFlagsAsync(It.IsAny<User>(), "hunter2", "INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 42 })),
            MailFlag.Seen, true, It.IsAny<CancellationToken>()), Times.Once);
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [1u], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task SetMessageFlags_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.SetFlagsAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<MailFlag>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to update the messages"));

        var result = await CreateController().SetMessageFlags(
            new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [1u], Flag = MailFlag.Seen, Value = true },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task Move_Returns204AndDelegates()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), "hunter2", "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), "Archive", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [42u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.MoveOrCopyAsync(It.IsAny<User>(), "hunter2", "INBOX",
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.MoveMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Move_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
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
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
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
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), "hunter2", "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), "Archive", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [42u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.MoveOrCopyAsync(It.IsAny<User>(), "hunter2", "INBOX",
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Copy_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to copy the messages"));

        var result = await CreateController().CopyMessages(
            new MoveMessagesRequest { FolderPath = "INBOX", Uids = [1u], TargetFolderPath = "Archive" },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204AndDelegates()
    {
        _messages.Setup(m => m.DeleteAsync(It.IsAny<User>(), "hunter2", "INBOX",
                It.IsAny<IReadOnlyList<uint>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = [42u] },
            CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _messages.Verify(m => m.DeleteAsync(It.IsAny<User>(), "hunter2", "INBOX",
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.DeleteMessages(
            new DeleteMessagesRequest { FolderPath = "INBOX", Uids = [1u] },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Delete_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.DeleteAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
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
        _messages.Setup(m => m.DeleteAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
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

    [Fact]
    public async Task EmptyFolder_Returns204AndDelegatesPurgeWhenNoTarget()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Trash", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Trash", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyFolder_DelegatesMoveWhenTargetGiven()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Projects", "Trash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "Trash" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), "hunter2", "Projects", "Trash", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyFolder_Returns400ForABlankSourceWithoutReachingTheRepository()
    {
        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = " ", TargetFolderPath = null }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyFolder_Returns400WhenTargetEqualsSource()
    {
        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "Projects" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("The target folder must differ from the source folder", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task EmptyFolder_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
            .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task EmptyFolder_Returns400WhenTargetIsNotSelectable()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ImapSession.TargetNotSelectable));

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "NoSelect" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task EmptyFolder_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to empty the folder"));

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
            .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", Quick = "x" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchMessages_returns_the_page()
    {
        _messages.Setup(m => m.SearchAsync(
                It.IsAny<User>(), "hunter2", "INBOX", true,
                It.Is<MailSearchCriteria>(c => c.Quick == "hello"), 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new MailSearchPage { Total = 2 }));

        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", AllFolders = true, Quick = "hello", Page = 2, PageSize = 25 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<MailSearchPage>(ok.Value);
        Assert.Equal(2, page.Total);
        _messages.Verify(m => m.SearchAsync(
            It.IsAny<User>(), "hunter2", "INBOX", true,
            It.Is<MailSearchCriteria>(c => c.Quick == "hello"), 2, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMessages_maps_server_failure_to_502()
    {
        _messages.Setup(m => m.SearchAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<MailSearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailSearchPage>("boom"));

        var result = await CreateController().SearchMessages(
            new SearchMessagesRequest { FolderPath = "INBOX", Quick = "x" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    // ── Attachment staging ──────────────────────────────────────────────

    [Fact]
    public async Task UploadAttachment_RefusesAMissingFile()
    {
        var result = await CreateController().UploadAttachment(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadAttachment_StoresUnderTheCallersAccount()
    {
        var info = new StagedAttachmentInfo(Guid.NewGuid(), "a.txt", 4, "text/plain");
        _staged.Setup(s => s.SaveAsync(
                WebmailUid.ToString(), "a.txt", "text/plain",
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(info));

        var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "a.txt")
        { Headers = new HeaderDictionary(), ContentType = "text/plain" };

        var result = await CreateController().UploadAttachment(file, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(info, ok.Value);
    }

    [Fact]
    public async Task UploadAttachment_AnswersBadRequestWhenTheStoreRefuses()
    {
        _staged.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<StagedAttachmentInfo>("The attachment exceeds the 25 MB limit"));

        var file = new FormFile(new MemoryStream([1]), 0, 1, "file", "big.bin")
        { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };

        var result = await CreateController().UploadAttachment(file, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result); // FromResult path: StatusCode(400, enveloppe)
    }

    [Fact]
    public void DeleteAttachment_IsIdempotentAndScoped()
    {
        var id = Guid.NewGuid();

        var result = CreateController().DeleteAttachment(id);

        Assert.IsType<NoContentResult>(result);
        _staged.Verify(s => s.Delete(WebmailUid.ToString(), id), Times.Once);
    }

    [Fact]
    public void GetStagedAttachment_ServesTheOwnersFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        try
        {
            var id = Guid.NewGuid();
            var info = new StagedAttachmentInfo(id, "logo.png", 3, "image/png", "logo@mail");
            _staged.Setup(s => s.Open(It.IsAny<string>(), id))
                .Returns(Result.Success(new StagedAttachment(info, path)));

            var controller = CreateController();
            var result = controller.GetStagedAttachment(id);

            var file = Assert.IsType<FileStreamResult>(result);
            Assert.Equal("image/png", file.ContentType);
            Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
            file.FileStream.Dispose(); // normally disposed by the MVC pipeline once the response is written
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GetStagedAttachment_AnswersNotFoundForAForeignId()
    {
        _staged.Setup(s => s.Open(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Result.Failure<StagedAttachment>("unknown_attachment"));

        var result = CreateController().GetStagedAttachment(Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── Send ────────────────────────────────────────────────────────────

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
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), "hunter2", It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
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
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, string, SendMessageRequest, CancellationToken>((_, _, r, _) => captured = r)
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
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<User, string, SendMessageRequest, CancellationToken>((_, _, r, _) => captured = r)
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.SendMessage(
            new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_MapsUnknownAttachmentToBadRequest()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SendMessageResult>(IMailSender.UnknownAttachment));

        var result = await CreateController().SendMessage(
            new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SendMessage_MapsAServerRefusalTo502()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
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
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SendMessageResult(false)));

        var result = await CreateController().SendMessage(
            new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(((SendMessageResult)ok.Value!).AppendedToSent);
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
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MimeMessage>(ImapSession.MessageNotFound));

        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "reply" }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrepareQuote_AnswersThePreparedQuote()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
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
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
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
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));

        var result = await controller.PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "reply" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrepareQuote_Returns502WhenImapFails()
    {
        _messages.Setup(m => m.GetMimeMessageAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 7u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MimeMessage>("Unable to read the message"));

        var result = await CreateController().PrepareQuote(
            new PrepareQuoteRequest { Folder = "INBOX", Uid = 7, Purpose = "reply" }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }
}
