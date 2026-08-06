using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class AdminRequestValidationTests
{
    [Fact]
    public void DomainRequest_WithoutAName_IsRefusedWithTheRepositorysWording()
    {
        Assert.Equal(["Domain name is required"], RequestValidation.Messages(new AdminDomainRequest()));
    }

    // The Id is deliberately unconstrained here: PUT /domains/{id} binds this body but takes
    // the id from the route, so a binding rule on Id would refuse a legal update. The create
    // side's 1-3 character rule lives in AdminRepository.
    [Fact]
    public void DomainRequest_DoesNotJudgeTheId_ThatIsTheRepositorysRule()
    {
        var request = new AdminDomainRequest { Id = "TOOLONG", Name = "example.org" };

        Assert.Empty(RequestValidation.Messages(request));
    }

    [Fact]
    public void VirtualDomainOwner_WithoutAUserId_IsRefused()
    {
        var messages = RequestValidation.Messages(new AdminVirtualDomainOwnerRequest());

        Assert.Equal(["A valid user id is required"], messages);
    }

    [Fact]
    public void VirtualDomainOwner_WithAPositiveUserId_IsValid()
    {
        Assert.Empty(RequestValidation.Messages(new AdminVirtualDomainOwnerRequest { UserId = 7 }));
    }
}
