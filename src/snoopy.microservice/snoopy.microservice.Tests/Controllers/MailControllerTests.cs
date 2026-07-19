using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class MailControllerTests
    {
        private readonly Mock<IMailFolderRepository> _folders = new();
        private readonly Mock<IMailMessageRepository> _messages = new();
        private readonly Mock<IMailCredentialStore> _credentials = new();
        private readonly Mock<IFolderRoleStore> _roleStore = new();

        private MailController CreateController()
        {
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));
            _roleStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<FolderRoleOverride>());

            return new MailController(_folders.Object, _messages.Object, _credentials.Object, _roleStore.Object)
            {
                ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be")
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
            _folders.Setup(f => f.DeleteFolderAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure("The inbox cannot be deleted"));

            var result = await CreateController().DeleteFolder(
                new DeleteFolderRequest { Path = "INBOX" }, CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        // ── Subscription ────────────────────────────────────────────────────

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SetFolderSubscription_Returns204AndPassesTheState(bool subscribed)
        {
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

        // ── Folder roles ────────────────────────────────────────────────────

        private static MailFolderNode RoleNode(string path, string? attributeRole = null, uint uidValidity = 1) =>
            new() { Path = path, Name = path, AttributeRole = attributeRole, UidValidity = uidValidity };

        private void SetupOverrides(params FolderRoleOverride[] rows)
            => _roleStore.Setup(s => s.GetAsync("alice@weesky.be", It.IsAny<CancellationToken>()))
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
            { AccountId = "alice@weesky.be", Role = "trash", FolderPath = "Corbeille", UidValidity = 1 });

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
            { AccountId = "alice@weesky.be", Role = "junk", FolderPath = "X", UidValidity = 1 });

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
            { AccountId = "alice@weesky.be", Role = "trash", FolderPath = "Corbeille", UidValidity = 1 });

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
        public async Task SetFolderRole_StoresTheLiveIdentityUnderTheCanonicalAccount()
        {
            SetupStatus("Corbeille", uidValidity: 77, mailboxId: "M1");
            SetupTree(RoleNode("Corbeille", uidValidity: 77));

            var result = await CreateController().SetFolderRole(
                new SetFolderRoleRequest { Role = "trash", FolderPath = "Corbeille" }, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            _roleStore.Verify(s => s.UpsertAsync(It.Is<FolderRoleOverride>(o =>
                o.AccountId == "alice@weesky.be" && o.Role == "trash" && o.FolderPath == "Corbeille"
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
            _roleStore.Verify(s => s.DeleteAsync("alice@weesky.be", "trash", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
