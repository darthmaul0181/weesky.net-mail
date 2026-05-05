using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class AliasesControllerTests
    {
        private readonly Mock<IAliasesRepository> _repo = new();

        private AliasesController CreateController()
        {
            var controller = new AliasesController(_repo.Object);
            controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
            return controller;
        }

        [Fact]
        public void Add_WhenSuccess_Returns204()
        {
            _repo.Setup(r => r.AddAlias(It.IsAny<User>(), It.IsAny<Alias>()))
                .Returns(Result.Success());

            var result = CreateController().Add(new Alias { Name = "johnny", Domain = "example.com" });

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(204, status.StatusCode);
        }

        [Fact]
        public void Add_WhenFailure_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.AddAlias(It.IsAny<User>(), It.IsAny<Alias>()))
                .Returns(Result.Failure("Alias already exists"));

            var result = CreateController().Add(new Alias { Name = "johnny", Domain = "example.com" });

            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void Add_WhenFailure_EnvelopeContainsErrorMessage()
        {
            _repo.Setup(r => r.AddAlias(It.IsAny<User>(), It.IsAny<Alias>()))
                .Returns(Result.Failure("Alias already exists"));

            var result = CreateController().Add(new Alias { Name = "johnny", Domain = "example.com" });

            var obj = Assert.IsType<ObjectResult>(result.Result);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("Alias already exists", envelope.Message);
        }

        [Fact]
        public void List_ReturnsAliasesFromRepository()
        {
            var aliases = new[] { new Alias { Name = "alias1", Domain = "example.com" } };
            _repo.Setup(r => r.GetAliases(It.IsAny<User>())).Returns(aliases);

            var result = CreateController().List();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(aliases, ok.Value);
        }

        [Fact]
        public void List_WithNoAliases_ReturnsEmptyCollection()
        {
            _repo.Setup(r => r.GetAliases(It.IsAny<User>())).Returns(Enumerable.Empty<Alias>());

            var result = CreateController().List();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Empty((IEnumerable<Alias>)ok.Value!);
        }

        [Fact]
        public void Delete_WhenSuccess_Returns204()
        {
            _repo.Setup(r => r.DeleteAlias(It.IsAny<User>(), It.IsAny<Alias>()))
                .Returns(Result.Success());

            var result = CreateController().Delete(new Alias { Name = "johnny", Domain = "example.com" });

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(204, status.StatusCode);
        }

        [Fact]
        public void Delete_WhenFailure_Returns400WithEnvelope()
        {
            _repo.Setup(r => r.DeleteAlias(It.IsAny<User>(), It.IsAny<Alias>()))
                .Returns(Result.Failure("Alias not found"));

            var result = CreateController().Delete(new Alias { Name = "johnny", Domain = "example.com" });

            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void Delete_PassesAuthenticatedUserToRepository()
        {
            _repo.Setup(r => r.DeleteAlias(It.IsAny<User>(), It.IsAny<Alias>()))
                .Returns(Result.Success());

            CreateController().Delete(new Alias { Name = "x", Domain = "example.com" });

            _repo.Verify(r => r.DeleteAlias(
                It.Is<User>(u => u.Name == "john" && u.Domain == "example.com"),
                It.IsAny<Alias>()),
                Times.Once);
        }
    }
}
