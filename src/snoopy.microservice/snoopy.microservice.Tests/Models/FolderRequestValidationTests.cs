using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

// The attributes carry the same wording as the controllers' own guards, so a request refused
// at the binding boundary answers the exact message a directly-invoked action answers.
public sealed class FolderRequestValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateFolder_WithoutAName_IsRefusedWithTheControllersWording(string name)
    {
        var messages = RequestValidation.Messages(new CreateFolderRequest { Name = name });

        Assert.Contains("A folder name is required", messages);
    }

    [Fact]
    public void CreateFolder_AtTheNamespaceRoot_IsValid()
    {
        Assert.Empty(RequestValidation.Messages(new CreateFolderRequest { Name = "Reports" }));
    }

    [Fact]
    public void RenameFolder_WithoutPathAndName_NamesBoth()
    {
        var messages = RequestValidation.Messages(new RenameFolderRequest());

        Assert.Contains("A folder path is required", messages);
        Assert.Contains("A folder name is required", messages);
    }

    [Fact]
    public void RenameFolder_ToTheRoot_IsValid()
    {
        var request = new RenameFolderRequest { Path = "Archive/Old", NewParentPath = "", NewName = "Old" };

        Assert.Empty(RequestValidation.Messages(request));
    }

    [Fact]
    public void DeleteFolder_WithoutAPath_IsRefused()
    {
        Assert.Contains("A folder path is required", RequestValidation.Messages(new DeleteFolderRequest()));
    }

    [Fact]
    public void FolderSubscription_WithoutAPath_IsRefused()
    {
        Assert.Contains("A folder path is required", RequestValidation.Messages(new FolderSubscriptionRequest()));
    }

    [Fact]
    public void FolderSubscription_WithAPath_IsValid()
    {
        Assert.Empty(RequestValidation.Messages(new FolderSubscriptionRequest { Path = "Newsletters" }));
    }
}
