using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class MailAttachmentsControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "hunter2");
    private static readonly string StagedScope = MailAccountConnection.StagedScope(
        new User("alice@weesky.be") { WebmailUid = WebmailUid }, MailAccountConnection.Primary);

    private static readonly MailAccountConnection ConnectedConn =
        TestConnections.Connected(Guid.NewGuid().ToString(), "alice@external.test", "other-secret");

    private readonly Mock<IAccountConnectionResolver> _connections = new();
    private readonly Mock<IStagedAttachmentStore> _staged = new();

    private MailAttachmentsController CreateController()
    {
        ResolveTo(Conn);

        return new MailAttachmentsController(_staged.Object, _connections.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", WebmailUid)
        };
    }

    /// <summary>Moq resolves overlapping setups by recency: call after <c>CreateController()</c>.</summary>
    private void ResolveTo(MailAccountConnection connection)
        => _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Result.Success(connection));

    // The composer stages under the account it is composing for, or Send — which reads the
    // account's own namespace — would never find the file again.
    [Fact]
    public async Task UploadAttachment_StagesUnderTheActiveAccount()
    {
        var controller = CreateController();
        ResolveTo(ConnectedConn);
        _staged.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new StagedAttachmentInfo(Guid.NewGuid(), "a.txt", 4, "text/plain")));
        var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "a.txt")
        { Headers = new HeaderDictionary(), ContentType = "text/plain" };

        await controller.UploadAttachment(file, inline: false, CancellationToken.None);

        _staged.Verify(s => s.SaveAsync(ConnectedConn.StagedScope(controller.AuthenticatedUser),
            "a.txt", "text/plain", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // An inline part is referenced from the body by cid; without an id assigned here the composer
    // could only ever produce attachments, whatever the body says.
    [Fact]
    public async Task UploadAttachment_AssignsAContentIdToAnInlineImage()
    {
        var controller = CreateController();
        ResolveTo(ConnectedConn);
        _staged.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success(new StagedAttachmentInfo(Guid.NewGuid(), "shot.png", 4, "image/png")));
        var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "shot.png")
        { Headers = new HeaderDictionary(), ContentType = "image/png" };

        await controller.UploadAttachment(file, inline: true, CancellationToken.None);

        _staged.Verify(s => s.SaveAsync(ConnectedConn.StagedScope(controller.AuthenticatedUser),
            "shot.png", "image/png", It.IsAny<Stream>(), It.IsAny<CancellationToken>(),
            It.Is<string>(id => !string.IsNullOrWhiteSpace(id))), Times.Once);
    }

    // A non-image inline part has nowhere to be shown: it would travel in the related part
    // referenced by nothing, which is the condition the send path's pruning exists to prevent.
    [Fact]
    public async Task UploadAttachment_RefusesANonImageInlinePart()
    {
        var controller = CreateController();
        ResolveTo(ConnectedConn);
        var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "a.pdf")
        { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

        var result = await controller.UploadAttachment(file, inline: true, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _staged.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAttachment_ScopesTheDeletionToTheActiveAccount()
    {
        var controller = CreateController();
        ResolveTo(ConnectedConn);
        var id = Guid.NewGuid();

        var result = await controller.DeleteAttachment(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _staged.Verify(s => s.Delete(ConnectedConn.StagedScope(controller.AuthenticatedUser), id), Times.Once);
    }

    // ── Attachment staging ──────────────────────────────────────────────

    [Fact]
    public async Task UploadAttachment_RefusesAMissingFile()
    {
        var result = await CreateController().UploadAttachment(null, inline: false, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadAttachment_StoresUnderTheCallersAccount()
    {
        var info = new StagedAttachmentInfo(Guid.NewGuid(), "a.txt", 4, "text/plain");
        _staged.Setup(s => s.SaveAsync(
                StagedScope, "a.txt", "text/plain",
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(info));

        var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "a.txt")
        { Headers = new HeaderDictionary(), ContentType = "text/plain" };

        var result = await CreateController().UploadAttachment(file, inline: false, CancellationToken.None);

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

        var result = await CreateController().UploadAttachment(file, inline: false, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result); // FromResult path: StatusCode(400, enveloppe)
    }

    [Fact]
    public async Task DeleteAttachment_IsIdempotentAndScoped()
    {
        var id = Guid.NewGuid();

        var result = await CreateController().DeleteAttachment(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _staged.Verify(s => s.Delete(StagedScope, id), Times.Once);
    }

    [Fact]
    public async Task GetStagedAttachment_ServesTheOwnersFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        try
        {
            var id = Guid.NewGuid();
            var info = new StagedAttachmentInfo(id, "logo.png", 3, "image/png", "logo@mail");
            _staged.Setup(s => s.Open(StagedScope, id))
                .Returns(Result.Success(new StagedAttachment(info, path)));

            var controller = CreateController();
            var result = await controller.GetStagedAttachment(id, CancellationToken.None);

            var file = Assert.IsType<FileStreamResult>(result);
            Assert.Equal("image/png", file.ContentType);
            Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
            file.FileStream.Dispose(); // normally disposed by the MVC pipeline once the response is written
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task GetStagedAttachment_AnswersNotFoundForAForeignId()
    {
        _staged.Setup(s => s.Open(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Result.Failure<StagedAttachment>("unknown_attachment"));

        var result = await CreateController().GetStagedAttachment(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
