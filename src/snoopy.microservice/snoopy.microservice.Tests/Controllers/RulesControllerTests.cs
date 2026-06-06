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
    public class RulesControllerTests
    {
        private readonly Mock<ISieveRepository> _repo = new();

        private RulesController CreateController()
        {
            var controller = new RulesController(_repo.Object);
            controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be");
            return controller;
        }

        // ----- GET /api/Rules -----

        [Fact]
        public async Task Get_WhenRepoSucceeds_Returns200WithRuleSet()
        {
            var ruleSet = new SieveRuleSet
            {
                Kind = SieveScriptKind.Structured,
                Rules = new[] { new SieveRule { Name = "x" } },
                RawScript = "# WEESKY-RULES-V1:..."
            };
            _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(ruleSet));

            var result = await CreateController().Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(ruleSet, ok.Value);
        }

        [Fact]
        public async Task Get_WhenRepoFails_Returns400WithErrorEnvelope()
        {
            _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<SieveRuleSet>("Connection refused"));

            var result = await CreateController().Get(CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            var envelope = Assert.IsType<ResultEnveloppe>(bad.Value);
            Assert.Equal("Connection refused", envelope.Message);
            Assert.Equal(ResultState.Error, envelope.State);
        }

        [Fact]
        public async Task Get_ForwardsAuthenticatedUser()
        {
            _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new SieveRuleSet()));

            await CreateController().Get(CancellationToken.None);

            _repo.Verify(r => r.GetRuleSetAsync(
                It.Is<User>(u => u.Name == "alice" && u.Domain == "weesky.be"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        // ----- PUT /api/Rules -----

        [Fact]
        public async Task Replace_WhenRepoSucceeds_Returns204()
        {
            _repo.Setup(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());

            var rules = new List<SieveRule> { new() { Name = "r1" } };
            var result = await CreateController().Replace(rules, CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(204, status.StatusCode);
        }

        [Fact]
        public async Task Replace_WhenRepoFails_Returns400WithErrorMessage()
        {
            _repo.Setup(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure("line 1: error: unknown command"));

            var result = await CreateController().Replace(new List<SieveRule>(), CancellationToken.None);

            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("line 1: error: unknown command", envelope.Message);
        }

        [Fact]
        public async Task Replace_WhenBodyIsNull_Returns400()
        {
            var result = await CreateController().Replace(null!, CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.IsType<ResultEnveloppe>(bad.Value);
            _repo.Verify(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Replace_ForwardsRulesToRepository()
        {
            _repo.Setup(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());
            var rules = new List<SieveRule> { new() { Name = "r1" }, new() { Name = "r2" } };

            await CreateController().Replace(rules, CancellationToken.None);

            _repo.Verify(r => r.SaveRulesAsync(
                It.IsAny<User>(),
                It.Is<IReadOnlyList<SieveRule>>(l => l.Count == 2 && l[0].Name == "r1" && l[1].Name == "r2"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        // ----- DELETE /api/Rules -----

        [Fact]
        public async Task DeleteAll_WhenRepoSucceeds_Returns204()
        {
            _repo.Setup(r => r.DeleteAllRulesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());

            var result = await CreateController().DeleteAll(CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(204, status.StatusCode);
        }

        [Fact]
        public async Task DeleteAll_WhenRepoFails_Returns400WithErrorMessage()
        {
            _repo.Setup(r => r.DeleteAllRulesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure("server down"));

            var result = await CreateController().DeleteAll(CancellationToken.None);

            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("server down", envelope.Message);
        }

        // ----- GET /api/Rules/Raw -----

        [Fact]
        public async Task GetRaw_WhenRepoSucceeds_Returns200WithContent()
        {
            const string raw = "require [\"fileinto\"];";
            _repo.Setup(r => r.GetRawScriptAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(raw));

            var result = await CreateController().GetRaw(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var body = Assert.IsType<SieveRawScript>(ok.Value);
            Assert.Equal(raw, body.Content);
        }

        [Fact]
        public async Task GetRaw_WhenRepoFails_Returns400()
        {
            _repo.Setup(r => r.GetRawScriptAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<string>("upstream"));

            var result = await CreateController().GetRaw(CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            var envelope = Assert.IsType<ResultEnveloppe>(bad.Value);
            Assert.Equal("upstream", envelope.Message);
        }

        // ----- PUT /api/Rules/Raw -----

        [Fact]
        public async Task PutRaw_WhenRepoSucceeds_Returns204()
        {
            _repo.Setup(r => r.SaveRawScriptAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());

            var result = await CreateController().PutRaw(new SieveRawScript { Content = "keep;" }, CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(204, status.StatusCode);
        }

        [Fact]
        public async Task PutRaw_WhenRepoFails_Returns400WithSieveCompilerError()
        {
            _repo.Setup(r => r.SaveRawScriptAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure("line 1: error: syntax"));

            var result = await CreateController().PutRaw(new SieveRawScript { Content = "garbage" }, CancellationToken.None);

            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("line 1: error: syntax", envelope.Message);
        }

        [Fact]
        public async Task PutRaw_WhenBodyIsNull_Returns400()
        {
            var result = await CreateController().PutRaw(null!, CancellationToken.None);

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.IsType<ResultEnveloppe>(bad.Value);
            _repo.Verify(r => r.SaveRawScriptAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PutRaw_WhenContentIsNull_ForwardsEmptyString()
        {
            _repo.Setup(r => r.SaveRawScriptAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());

            await CreateController().PutRaw(new SieveRawScript { Content = null! }, CancellationToken.None);

            _repo.Verify(r => r.SaveRawScriptAsync(It.IsAny<User>(), string.Empty, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
