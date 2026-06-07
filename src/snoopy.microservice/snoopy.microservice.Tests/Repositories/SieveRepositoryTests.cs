using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.RuleProviders.Rainloop;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class SieveRepositoryTests
    {
        private const string WeeskyScriptName = "weesky-rules";
        private const string RainloopScriptName = "rainloop.user";

        private static User Alice => new("alice@weesky.be");

        private static (SieveRepository repo, Mock<IManageSieveClient> client, Mock<IManageSieveSession> session) CreateSut()
        {
            var session = new Mock<IManageSieveSession>();
            var client = new Mock<IManageSieveClient>();
            client.Setup(c => c.OpenSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Result.Success<IManageSieveSession>(session.Object));

            // Default so SaveRulesAsync's post-activation cleanup finds nothing to remove.
            // Tests that care about listing override this via SetupList.
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(Array.Empty<SieveScriptListEntry>()));

            var registry = new RuleProviderRegistry(new IRuleProvider[]
            {
                new WeeskyRuleProvider(),
                new RainloopRuleProvider()
            });
            var options = Options.Create(new SieveOptions());
            var repo = new SieveRepository(client.Object, registry, options, Mock.Of<ILogger<SieveRepository>>());
            return (repo, client, session);
        }

        private static void SetupList(Mock<IManageSieveSession> session, params SieveScriptListEntry[] entries) =>
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(entries));

        // ----- GetRuleSetAsync -----

        [Fact]
        public async Task GetRuleSetAsync_WhenSessionFails_ReturnsFailure()
        {
            var client = new Mock<IManageSieveClient>();
            client.Setup(c => c.OpenSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Result.Failure<IManageSieveSession>("Connection refused"));
            var registry = new RuleProviderRegistry(new IRuleProvider[] { new WeeskyRuleProvider() });
            var repo = new SieveRepository(client.Object, registry, Options.Create(new SieveOptions()), Mock.Of<ILogger<SieveRepository>>());

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsFailure);
            Assert.Equal("Connection refused", result.Error);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenNoScripts_ReturnsEmptyStructuredOnNewAccountDefault()
        {
            var (repo, _, session) = CreateSut();
            SetupList(session);

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Structured, result.Value.Kind);
            Assert.Empty(result.Value.Rules);
            // New accounts start on Rainloop so the Snappymail webmail stays in sync by default.
            Assert.Equal("rainloop", result.Value.ProviderId);
            Assert.Equal(RainloopScriptName, result.Value.ScriptName);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenWeeskyScriptExists_LoadsItViaWeeskyProvider()
        {
            var weesky = new WeeskyRuleProvider();
            var rules = new[]
            {
                new SieveRule
                {
                    Id = Guid.NewGuid(),
                    Name = "Alerts",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "[ALERT]" } },
                    Actions = { new SieveAction { Type = SieveActionType.FileInto, Argument = "Alerts" } }
                }
            };
            var script = weesky.Compile(rules).Value;

            var (repo, _, session) = CreateSut();
            SetupList(session, new SieveScriptListEntry(WeeskyScriptName, true));
            session.Setup(s => s.GetScriptAsync(WeeskyScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(script));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Structured, result.Value.Kind);
            Assert.Equal("weesky", result.Value.ProviderId);
            Assert.Equal(WeeskyScriptName, result.Value.ScriptName);
            Assert.Single(result.Value.Rules);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenRainloopScriptExists_LoadsItViaRainloopProvider()
        {
            var rainloop = new RainloopRuleProvider();
            var rules = new[]
            {
                new SieveRule
                {
                    Id = Guid.NewGuid(),
                    Name = "Zik",
                    StopAfter = true,
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "[ZIK]" } },
                    Actions =
                    {
                        new SieveAction { Type = SieveActionType.SetFlag, Argument = @"\Seen" },
                        new SieveAction { Type = SieveActionType.FileInto, Argument = "Zik" }
                    }
                }
            };
            var script = rainloop.Compile(rules).Value;

            var (repo, _, session) = CreateSut();
            SetupList(session, new SieveScriptListEntry(RainloopScriptName, true));
            session.Setup(s => s.GetScriptAsync(RainloopScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(script));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Structured, result.Value.Kind);
            Assert.Equal("rainloop", result.Value.ProviderId);
            Assert.Equal(RainloopScriptName, result.Value.ScriptName);
            Assert.Single(result.Value.Rules);
            Assert.Equal("Zik", result.Value.Rules[0].Name);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenUnknownActiveScript_ReturnsAdvancedWithRaw()
        {
            const string raw = "require [\"fileinto\"];\nfileinto \"Inbox\";";
            var (repo, _, session) = CreateSut();
            SetupList(session, new SieveScriptListEntry("custom", true));
            session.Setup(s => s.GetScriptAsync("custom", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(raw));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Advanced, result.Value.Kind);
            Assert.Null(result.Value.ProviderId);
            Assert.Equal("custom", result.Value.ScriptName);
            Assert.Equal(raw, result.Value.RawScript);
        }

        [Fact]
        public async Task GetRuleSetAsync_PrefersWeeskyOverActiveRainloopWhenBothExist()
        {
            var weesky = new WeeskyRuleProvider();
            var script = weesky.Compile(Array.Empty<SieveRule>()).Value;

            var (repo, _, session) = CreateSut();
            SetupList(session,
                new SieveScriptListEntry(WeeskyScriptName, false),
                new SieveScriptListEntry(RainloopScriptName, true));
            session.Setup(s => s.GetScriptAsync(WeeskyScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(script));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(WeeskyScriptName, result.Value.ScriptName);
            session.Verify(s => s.GetScriptAsync(RainloopScriptName, It.IsAny<CancellationToken>()), Times.Never);
        }

        // ----- SaveRulesAsync -----

        [Fact]
        public async Task SaveRulesAsync_WithDefaultProvider_WritesToWeeskyScript()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(WeeskyScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(WeeskyScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var rules = new[]
            {
                new SieveRule
                {
                    Name = "x",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions = { new SieveAction { Type = SieveActionType.Keep } }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, rules, providerId: null, scriptName: null);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.PutScriptAsync(WeeskyScriptName,
                It.Is<string>(c => c.Contains("# WEESKY-RULES-V1:")), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveRulesAsync_WithRainloopProvider_WritesToRainloopScript()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(RainloopScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(RainloopScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var rules = new[]
            {
                new SieveRule
                {
                    Name = "x",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions = { new SieveAction { Type = SieveActionType.FileInto, Argument = "X" } }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, rules, providerId: "rainloop", scriptName: null);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.PutScriptAsync(RainloopScriptName,
                It.Is<string>(c => c.Contains("# RAINLOOP:SIEVE")), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveRulesAsync_DeletesOtherProvidersManagedScript()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(WeeskyScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(WeeskyScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            // After activating weesky-rules, an inactive rainloop.user still lingers.
            SetupList(session,
                new SieveScriptListEntry(WeeskyScriptName, true),
                new SieveScriptListEntry(RainloopScriptName, false));
            session.Setup(s => s.DeleteScriptAsync(RainloopScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var rules = new[]
            {
                new SieveRule
                {
                    Name = "x",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions = { new SieveAction { Type = SieveActionType.Keep } }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, rules, providerId: "weesky", scriptName: null);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.DeleteScriptAsync(RainloopScriptName, It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.DeleteScriptAsync(WeeskyScriptName, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SaveRulesAsync_DoesNotDeleteUnknownOrActiveScripts()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(WeeskyScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(WeeskyScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            SetupList(session,
                new SieveScriptListEntry(WeeskyScriptName, true),
                new SieveScriptListEntry("custom-advanced", false));

            var rules = new[]
            {
                new SieveRule
                {
                    Name = "x",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions = { new SieveAction { Type = SieveActionType.Keep } }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, rules, providerId: "weesky", scriptName: null);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.DeleteScriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SaveRulesAsync_WithExplicitScriptName_WritesToThatScript()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync("custom-name", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync("custom-name", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var rules = new[]
            {
                new SieveRule
                {
                    Name = "x",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions = { new SieveAction { Type = SieveActionType.Keep } }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, rules, providerId: null, scriptName: "custom-name");

            Assert.True(result.IsSuccess);
            session.Verify(s => s.PutScriptAsync("custom-name", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveRulesAsync_UnknownProvider_ReturnsFailure()
        {
            var (repo, _, session) = CreateSut();

            var result = await repo.SaveRulesAsync(Alice, Array.Empty<SieveRule>(), providerId: "nope", scriptName: null);

            Assert.True(result.IsFailure);
            session.Verify(s => s.PutScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SaveRulesAsync_WhenProviderRejectsCompile_DoesNotCallServer()
        {
            var (repo, _, session) = CreateSut();
            var invalid = new[]
            {
                new SieveRule
                {
                    Name = "Multi",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions =
                    {
                        new SieveAction { Type = SieveActionType.FileInto, Argument = "A" },
                        new SieveAction { Type = SieveActionType.Redirect, Argument = "y@z.com" }
                    }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, invalid, providerId: "rainloop", scriptName: null);

            Assert.True(result.IsFailure);
            Assert.Contains("Rainloop", result.Error);
            session.Verify(s => s.PutScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ----- SaveRawScriptAsync -----

        [Fact]
        public async Task SaveRawScriptAsync_WithScriptName_WritesToThatScript()
        {
            const string raw = "require [\"fileinto\"];\nfileinto \"Inbox\";";
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(RainloopScriptName, raw, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(RainloopScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.SaveRawScriptAsync(Alice, raw, RainloopScriptName);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.PutScriptAsync(RainloopScriptName, raw, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveRawScriptAsync_WithoutScriptName_UsesDefault()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(WeeskyScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(WeeskyScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            await repo.SaveRawScriptAsync(Alice, "stop;", scriptName: null);

            session.Verify(s => s.PutScriptAsync(WeeskyScriptName, "stop;", It.IsAny<CancellationToken>()), Times.Once);
        }

        // ----- DeleteAllRulesAsync -----

        [Fact]
        public async Task DeleteAllRulesAsync_WhenScriptMissing_IsNoOp()
        {
            var (repo, _, session) = CreateSut();
            SetupList(session);

            var result = await repo.DeleteAllRulesAsync(Alice);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.DeleteScriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DeleteAllRulesAsync_WhenActiveWeeskyScript_DeactivatesThenDeletes()
        {
            var (repo, _, session) = CreateSut();
            SetupList(session, new SieveScriptListEntry(WeeskyScriptName, true));
            session.Setup(s => s.SetActiveAsync(string.Empty, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.DeleteScriptAsync(WeeskyScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.DeleteAllRulesAsync(Alice);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.SetActiveAsync(string.Empty, It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.DeleteScriptAsync(WeeskyScriptName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAllRulesAsync_WhenActiveRainloopScript_DeletesIt()
        {
            var (repo, _, session) = CreateSut();
            SetupList(session, new SieveScriptListEntry(RainloopScriptName, true));
            session.Setup(s => s.SetActiveAsync(string.Empty, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.DeleteScriptAsync(RainloopScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.DeleteAllRulesAsync(Alice);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.DeleteScriptAsync(RainloopScriptName, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
