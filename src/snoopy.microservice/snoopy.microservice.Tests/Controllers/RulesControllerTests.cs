using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.RuleProviders.Rainloop;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class RulesControllerTests
    {
        private readonly Mock<ISieveRepository> _repo = new();
        private readonly IRuleProviderRegistry _registry = new RuleProviderRegistry(
            new IRuleProvider[] { new WeeskyRuleProvider(), new RainloopRuleProvider() });

        private RulesController CreateController()
        {
            var controller = new RulesController(_repo.Object, _registry);
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
                ProviderId = "weesky",
                ScriptName = "weesky-rules"
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
        }

        // ----- PUT /api/Rules -----

        [Fact]
        public async Task Replace_ForwardsProviderAndScriptName()
        {
            _repo.Setup(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                              "rainloop", "rainloop.user", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());

            var body = new SaveRulesRequest
            {
                Rules = new List<SieveRule> { new() { Name = "r1" } },
                ProviderId = "rainloop",
                ScriptName = "rainloop.user"
            };

            var result = await CreateController().Replace(body, CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(204, status.StatusCode);
            _repo.Verify(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                               "rainloop", "rainloop.user", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Replace_WithNullProvider_ForwardsAsNull()
        {
            _repo.Setup(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                              null, null, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());

            await CreateController().Replace(new SaveRulesRequest(), CancellationToken.None);

            _repo.Verify(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                               null, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Replace_WhenRepoFails_Returns400()
        {
            _repo.Setup(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                              It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure("compiler error"));

            var result = await CreateController().Replace(new SaveRulesRequest(), CancellationToken.None);

            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(400, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("compiler error", envelope.Message);
        }

        [Fact]
        public async Task Replace_NullBody_Returns400()
        {
            var result = await CreateController().Replace(null!, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            _repo.Verify(r => r.SaveRulesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                               It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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

        // ----- GET /api/Rules/Raw -----

        [Fact]
        public async Task GetRaw_ReturnsContentAndScriptNameFromRuleSet()
        {
            const string raw = "require [\"fileinto\"];";
            _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(new SieveRuleSet
                 {
                     Kind = SieveScriptKind.Advanced,
                     RawScript = raw,
                     ScriptName = "rainloop.user"
                 }));

            var result = await CreateController().GetRaw(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var body = Assert.IsType<SieveRawScript>(ok.Value);
            Assert.Equal(raw, body.Content);
            Assert.Equal("rainloop.user", body.ScriptName);
        }

        // ----- PUT /api/Rules/Raw -----

        [Fact]
        public async Task PutRaw_ForwardsScriptName()
        {
            _repo.Setup(r => r.SaveRawScriptAsync(It.IsAny<User>(), It.IsAny<string>(), "rainloop.user", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success());

            var result = await CreateController().PutRaw(new SieveRawScript { Content = "keep;", ScriptName = "rainloop.user" }, CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(204, status.StatusCode);
            _repo.Verify(r => r.SaveRawScriptAsync(It.IsAny<User>(), "keep;", "rainloop.user", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PutRaw_NullBody_Returns400()
        {
            var result = await CreateController().PutRaw(null!, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            _repo.Verify(r => r.SaveRawScriptAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ----- GET /api/Rules/Providers -----

        [Fact]
        public void ListProviders_ReturnsAllRegisteredProvidersWithDefaultFlag()
        {
            var result = CreateController().ListProviders();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var infos = Assert.IsAssignableFrom<IEnumerable<RuleProviderInfo>>(ok.Value);
            var list = infos.ToList();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, p => p.Id == "weesky" && p.IsDefault);
            Assert.Contains(list, p => p.Id == "rainloop" && !p.IsDefault);
        }
    }
}
