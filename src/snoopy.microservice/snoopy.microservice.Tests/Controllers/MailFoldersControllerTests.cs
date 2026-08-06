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

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class MailFoldersControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "hunter2");

    private static readonly string ConnectedId = Guid.NewGuid().ToString();
    private static readonly MailAccountConnection ConnectedConn =
        TestConnections.Connected(ConnectedId, "alice@external.test", "other-secret");

    private readonly Mock<IMailFolderRepository> _folders = new();
    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();
    private readonly Mock<IFolderRoleStore> _roleStore = new();

    private MailFoldersController CreateController()
    {
        ResolveTo(Conn);
        _roleStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<FolderRoleOverride>());

        return new MailFoldersController(_folders.Object, _messages.Object, _connections.Object, _roleStore.Object)
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

    private void SetupTree(params MailFolderNode[] nodes)
        => _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
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

        _folders.Verify(f => f.GetTreeAsync(It.IsAny<User>(), Conn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFolders_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.GetFolders(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ResultEnveloppe>(unauthorized.Value);
        Assert.Equal("credentials_unavailable", envelope.Message);
    }

    [Fact]
    public async Task GetFolders_DoesNotReachTheRepositoryWithoutCredentials()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        await controller.GetFolders(CancellationToken.None);

        _folders.Verify(
            f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetFolders_Returns502WhenImapFails()
    {
        _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
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

    // ── The active account ──────────────────────────────────────────────
    // A connected account whose stored secret no longer decrypts must not answer 401: the
    // client's global 401 handler signs the user out, and the main session is perfectly valid.

    [Fact]
    public async Task GetFolders_AnswersConflictWhenTheConnectedCredentialsAreInvalid()
    {
        var controller = CreateController();
        FailResolution(ConnectedAccountErrors.CredentialsInvalid);

        var result = await controller.GetFolders(CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, Assert.IsType<ResultEnveloppe>(conflict.Value).Message);
        _folders.Verify(f => f.GetTreeAsync(
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // An unknown id and another user's id are the same answer, by design.
    [Fact]
    public async Task GetFolders_AnswersNotFoundForAForeignAccount()
    {
        var controller = CreateController();
        FailResolution(ConnectedAccountErrors.AccountNotFound);

        var result = await controller.GetFolders(CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(ConnectedAccountErrors.AccountNotFound, Assert.IsType<ResultEnveloppe>(notFound.Value).Message);
    }

    [Fact]
    public async Task GetFolders_ScopesTheRoleOverridesToTheAccount()
    {
        var controller = CreateController();
        SetupTree(RoleNode("INBOX", attributeRole: "inbox"));
        ResolveTo(ConnectedConn);

        await controller.GetFolders(CancellationToken.None);

        _roleStore.Verify(s => s.GetAsync(WebmailUid, ConnectedId, It.IsAny<CancellationToken>()), Times.Once);
        _roleStore.Verify(s => s.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetFolderRole_StoresTheOverrideUnderTheAccount()
    {
        var controller = CreateController();
        SetupStatus("Corbeille", uidValidity: 3);
        SetupTree(RoleNode("Corbeille", uidValidity: 3));
        ResolveTo(ConnectedConn);

        var result = await controller.SetFolderRole(
            new SetFolderRoleRequest { Role = "trash", FolderPath = "Corbeille" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _roleStore.Verify(s => s.UpsertAsync(
            It.Is<FolderRoleOverride>(o => o.UserId == WebmailUid && o.AccountId == ConnectedId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearFolderRole_ScopesTheDeletionToTheAccount()
    {
        var controller = CreateController();
        ResolveTo(ConnectedConn);

        var result = await controller.ClearFolderRole("trash", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _roleStore.Verify(s => s.DeleteAsync(WebmailUid, ConnectedId, "trash", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateFolder_ReturnsTheNewPath()
    {
        _folders.Setup(f => f.CreateFolderAsync(It.IsAny<User>(), Conn, "INBOX", "Projects", It.IsAny<CancellationToken>()))
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
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateFolder_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.CreateFolder(
            new CreateFolderRequest { Name = "Projects" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateFolder_Returns502WhenImapFails()
    {
        _folders.Setup(f => f.CreateFolderAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        _folders.Setup(f => f.RenameFolderAsync(It.IsAny<User>(), Conn, "Old", "INBOX", "New", It.IsAny<CancellationToken>()))
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
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteFolder_Returns204OnSuccess()
    {
        SetupTree(RoleNode("Projects"));
        _folders.Setup(f => f.DeleteFolderAsync(It.IsAny<User>(), Conn, "Projects", It.IsAny<CancellationToken>()))
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
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteFolder_Returns502WhenTheServerRefuses()
    {
        SetupTree(RoleNode("Projects"));
        _folders.Setup(f => f.DeleteFolderAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        _folders.Setup(f => f.SetSubscriptionAsync(It.IsAny<User>(), Conn, "Projects", subscribed, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

        var result = await CreateController().SetFolderSubscription(
            new FolderSubscriptionRequest { Path = "Projects", Subscribed = subscribed }, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        _folders.Verify(f => f.SetSubscriptionAsync(
            It.IsAny<User>(), Conn, "Projects", subscribed, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFolderSubscription_Returns401WhenCredentialsAreUnavailable()
    {
        var controller = CreateController();
        FailResolution("credentials_unavailable");

        var result = await controller.SetFolderSubscription(
            new FolderSubscriptionRequest { Path = "Projects", Subscribed = true }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
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
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteFolder_RefusesTheInbox()
    {
        SetupTree(RoleNode("INBOX", attributeRole: "inbox"));

        var result = await CreateController().DeleteFolder(
            new DeleteFolderRequest { Path = "INBOX" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _folders.Verify(f => f.DeleteFolderAsync(
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
            It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Only hiding is refused.
    [Fact]
    public async Task SetFolderSubscription_AllowsShowingAFolderHoldingARole()
    {
        SetupTree(RoleNode("Corbeille", attributeRole: "trash"));
        _folders.Setup(f => f.SetSubscriptionAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "Corbeille", true, It.IsAny<CancellationToken>()))
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
        => _roleStore.Setup(s => s.GetAsync(WebmailUid, AccountScope.Primary, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(rows.ToList());

    private void SetupStatus(string path, uint uidValidity = 1, string? mailboxId = null, bool selectable = true)
        => _folders.Setup(f => f.GetFolderStatusAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), path, It.IsAny<CancellationToken>()))
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
        var result = await CreateController().SetFolderRole(null!, CancellationToken.None);

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
        _folders.Setup(f => f.GetFolderStatusAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), "Gone", It.IsAny<CancellationToken>()))
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
            o.AccountId == AccountScope.Primary && o.Role == "junk" && o.FolderPath == "Corbeille"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFolderRole_Returns502WhenTheTreeCannotBeRead()
    {
        var controller = CreateController();
        SetupStatus("X");
        _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
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
            o.UserId == WebmailUid && o.AccountId == AccountScope.Primary
            && o.Role == "trash" && o.FolderPath == "Corbeille"
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
        _roleStore.Verify(s => s.DeleteAsync(WebmailUid, AccountScope.Primary, "trash", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Empty ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyFolder_Returns204AndDelegatesPurgeWhenNoTarget()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), Conn, "Trash", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), Conn, "Trash", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyFolder_DelegatesMoveWhenTargetGiven()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), Conn, "Projects", "Trash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "Trash" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), Conn, "Projects", "Trash", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyFolder_Returns400ForABlankSourceWithoutReachingTheRepository()
    {
        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = " ", TargetFolderPath = null }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _messages.Verify(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
        FailResolution("credentials_unavailable");

        var result = await controller.EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task EmptyFolder_Returns400WhenTargetIsNotSelectable()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(ImapSession.TargetNotSelectable));

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Projects", TargetFolderPath = "NoSelect" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task EmptyFolder_Returns502WhenTheServerRefuses()
    {
        _messages.Setup(m => m.EmptyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to empty the folder"));

        var result = await CreateController().EmptyFolder(
            new EmptyFolderRequest { FolderPath = "Trash", TargetFolderPath = null }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
