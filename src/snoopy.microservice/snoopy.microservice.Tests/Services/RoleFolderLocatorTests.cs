using CSharpFunctionalExtensions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

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
