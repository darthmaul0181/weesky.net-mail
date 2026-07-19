using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class AliasesControllerTests
{
    private readonly Mock<IAliasesRepository> _repo = new();

    private AliasesController CreateController()
    {
        var controller = new AliasesController(_repo.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
        return controller;
    }

    [Fact]
    public async Task Add_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.AddAliasAsync(It.IsAny<User>(), It.IsAny<Alias>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().Add(new Alias { Name = "johnny", Domain = "example.com" });

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(204, status.StatusCode);
    }

    [Fact]
    public async Task Add_WhenFailure_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.AddAliasAsync(It.IsAny<User>(), It.IsAny<Alias>()))
            .ReturnsAsync(Result.Failure("Alias already exists"));

        var result = await CreateController().Add(new Alias { Name = "johnny", Domain = "example.com" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task Add_WhenFailure_EnvelopeContainsErrorMessage()
    {
        _repo.Setup(r => r.AddAliasAsync(It.IsAny<User>(), It.IsAny<Alias>()))
            .ReturnsAsync(Result.Failure("Alias already exists"));

        var result = await CreateController().Add(new Alias { Name = "johnny", Domain = "example.com" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Alias already exists", envelope.Message);
    }

    [Fact]
    public async Task List_ReturnsAliasesFromRepository()
    {
        var aliases = new[] { new Alias { Name = "alias1", Domain = "example.com" } };
        _repo.Setup(r => r.GetAliasesAsync(It.IsAny<User>())).ReturnsAsync(aliases);

        var result = await CreateController().List();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(aliases, ok.Value);
    }

    [Fact]
    public async Task List_WithNoAliases_ReturnsEmptyCollection()
    {
        _repo.Setup(r => r.GetAliasesAsync(It.IsAny<User>())).ReturnsAsync(Enumerable.Empty<Alias>());

        var result = await CreateController().List();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty((IEnumerable<Alias>)ok.Value!);
    }

    [Fact]
    public async Task Delete_WhenSuccess_Returns204()
    {
        _repo.Setup(r => r.DeleteAliasAsync(It.IsAny<User>(), It.IsAny<Alias>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateController().Delete(new Alias { Name = "johnny", Domain = "example.com" });

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(204, status.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenFailure_Returns400WithEnvelope()
    {
        _repo.Setup(r => r.DeleteAliasAsync(It.IsAny<User>(), It.IsAny<Alias>()))
            .ReturnsAsync(Result.Failure("Alias not found"));

        var result = await CreateController().Delete(new Alias { Name = "johnny", Domain = "example.com" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task Delete_PassesAuthenticatedUserToRepository()
    {
        _repo.Setup(r => r.DeleteAliasAsync(It.IsAny<User>(), It.IsAny<Alias>()))
            .ReturnsAsync(Result.Success());

        await CreateController().Delete(new Alias { Name = "x", Domain = "example.com" });

        _repo.Verify(r => r.DeleteAliasAsync(
            It.Is<User>(u => u.Name == "john" && u.Domain == "example.com"),
            It.IsAny<Alias>()),
            Times.Once);
    }
}
