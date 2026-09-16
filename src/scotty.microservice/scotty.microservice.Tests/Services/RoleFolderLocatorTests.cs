using CSharpFunctionalExtensions;
using Moq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class RoleFolderLocatorTests
{
    [Fact]
    public async Task Find_ReturnsTheFolderHoldingTheRole_NullWhenNone()
    {
        var folders = new Mock<IMailFolderRepository>();
        var roles = new Mock<IFolderRoleStore>();
        var user = new User("mick@weesky.be") { WebmailUid = Guid.NewGuid() };
        var conn = TestConnections.Primary("mick@weesky.be", "pw");
        var trash = new MailFolderNode { Name = "Trash", Path = "Trash", AttributeRole = "trash", Selectable = true };
        folders.Setup(f => f.GetTreeAsync(user, conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([trash]));
        roles.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FolderRoleOverride>());
        var locator = new RoleFolderLocator(folders.Object, roles.Object);

        Assert.Equal("Trash", await locator.FindAsync(user, conn, "trash", CancellationToken.None));
        Assert.Null(await locator.FindAsync(user, conn, "sent", CancellationToken.None));
    }
}
