using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class SieveRepositoryTests
    {
        private const string ScriptName = "weesky-rules";

        private static User Alice => new("alice@weesky.be");

        private static (SieveRepository repo, Mock<IManageSieveClient> client, Mock<IManageSieveSession> session) CreateSut()
        {
            var session = new Mock<IManageSieveSession>();
            var client = new Mock<IManageSieveClient>();
            client.Setup(c => c.OpenSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Result.Success<IManageSieveSession>(session.Object));

            var options = Options.Create(new SieveOptions { ScriptName = ScriptName });
            var repo = new SieveRepository(client.Object, new SieveScriptCompiler(), options, Mock.Of<ILogger<SieveRepository>>());
            return (repo, client, session);
        }

        // ----- GetRuleSetAsync -----

        [Fact]
        public async Task GetRuleSetAsync_WhenSessionFails_ReturnsFailure()
        {
            var client = new Mock<IManageSieveClient>();
            client.Setup(c => c.OpenSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Result.Failure<IManageSieveSession>("Connection refused"));
            var repo = new SieveRepository(client.Object, new SieveScriptCompiler(),
                Options.Create(new SieveOptions { ScriptName = ScriptName }), Mock.Of<ILogger<SieveRepository>>());

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsFailure);
            Assert.Equal("Connection refused", result.Error);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenScriptDoesNotExist_ReturnsEmptyStructured()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry("something-else", false) }));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Structured, result.Value.Kind);
            Assert.Empty(result.Value.Rules);
            Assert.Equal(string.Empty, result.Value.RawScript);
            session.Verify(s => s.GetScriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenManagedScriptMissingButAnotherActive_AdoptsActiveScript()
        {
            const string rainloopScript = "require [\"fileinto\"];\nif header :contains \"Subject\" \"x\" { fileinto \"X\"; }";

            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[]
                   {
                       new SieveScriptListEntry("rainloop.user.sieve", true),
                       new SieveScriptListEntry("backup", false)
                   }));
            session.Setup(s => s.GetScriptAsync("rainloop.user.sieve", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(rainloopScript));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Advanced, result.Value.Kind);
            Assert.Equal(rainloopScript, result.Value.RawScript);
            Assert.Equal("rainloop.user.sieve", result.Value.AdoptedFromScriptName);
            session.Verify(s => s.GetScriptAsync("rainloop.user.sieve", It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.GetScriptAsync(ScriptName, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenOnlyInactiveScriptsExist_ReturnsEmpty()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[]
                   {
                       new SieveScriptListEntry("old", false)
                   }));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Structured, result.Value.Kind);
            Assert.Empty(result.Value.Rules);
            Assert.Null(result.Value.AdoptedFromScriptName);
            session.Verify(s => s.GetScriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenManagedScriptExists_PrefersItOverActiveOther()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[]
                   {
                       new SieveScriptListEntry(ScriptName, false),
                       new SieveScriptListEntry("rainloop", true)
                   }));
            session.Setup(s => s.GetScriptAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success("require [\"fileinto\"];"));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.AdoptedFromScriptName);
            session.Verify(s => s.GetScriptAsync(ScriptName, It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.GetScriptAsync("rainloop", It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenScriptHasMarker_ReturnsStructuredRules()
        {
            var compiler = new SieveScriptCompiler();
            var rules = new[]
            {
                new SieveRule
                {
                    Id = Guid.NewGuid(),
                    Name = "Move alerts",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "[ALERT]" } },
                    Actions = { new SieveAction { Type = SieveActionType.FileInto, Argument = "Alerts" } }
                }
            };
            var script = compiler.Compile(rules).Value;

            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry(ScriptName, true) }));
            session.Setup(s => s.GetScriptAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(script));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Structured, result.Value.Kind);
            Assert.Single(result.Value.Rules);
            Assert.Equal("Move alerts", result.Value.Rules[0].Name);
            Assert.Equal(script, result.Value.RawScript);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenScriptHasNoMarker_ReturnsAdvancedWithRaw()
        {
            const string raw = "require [\"fileinto\"];\nfileinto \"Inbox\";";

            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry(ScriptName, true) }));
            session.Setup(s => s.GetScriptAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(raw));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(SieveScriptKind.Advanced, result.Value.Kind);
            Assert.Empty(result.Value.Rules);
            Assert.Equal(raw, result.Value.RawScript);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenListFails_ReturnsFailure()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IReadOnlyList<SieveScriptListEntry>>("server down"));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsFailure);
            Assert.Equal("server down", result.Error);
        }

        [Fact]
        public async Task GetRuleSetAsync_WhenGetScriptFails_ReturnsFailure()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry(ScriptName, true) }));
            session.Setup(s => s.GetScriptAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<string>("denied"));

            var result = await repo.GetRuleSetAsync(Alice);

            Assert.True(result.IsFailure);
            Assert.Equal("denied", result.Error);
        }

        // ----- GetRawScriptAsync -----

        [Fact]
        public async Task GetRawScriptAsync_ReturnsRawContent()
        {
            const string raw = "# WEESKY-RULES-V1:eyJydWxlcyI6W119\n";
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry(ScriptName, true) }));
            session.Setup(s => s.GetScriptAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(raw));

            var result = await repo.GetRawScriptAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(raw, result.Value);
        }

        [Fact]
        public async Task GetRawScriptAsync_WhenScriptMissing_ReturnsEmptyString()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(Array.Empty<SieveScriptListEntry>()));

            var result = await repo.GetRawScriptAsync(Alice);

            Assert.True(result.IsSuccess);
            Assert.Equal(string.Empty, result.Value);
        }

        // ----- SaveRulesAsync -----

        [Fact]
        public async Task SaveRulesAsync_WithValidRules_PutsThenActivates()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(ScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var rules = new[]
            {
                new SieveRule
                {
                    Name = "Trash newsletters",
                    Conditions = { new SieveCondition { Field = SieveConditionField.From, Operator = SieveConditionOperator.Contains, Value = "noreply" } },
                    Actions = { new SieveAction { Type = SieveActionType.FileInto, Argument = "Junk" } }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, rules);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.PutScriptAsync(ScriptName, It.Is<string>(c => c.Contains("# WEESKY-RULES-V1:")), It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.SetActiveAsync(ScriptName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveRulesAsync_WhenCompilerValidationFails_DoesNotCallServer()
        {
            var (repo, _, session) = CreateSut();
            var invalid = new[] { new SieveRule { Name = "" } };

            var result = await repo.SaveRulesAsync(Alice, invalid);

            Assert.True(result.IsFailure);
            session.Verify(s => s.PutScriptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SaveRulesAsync_WhenPutFails_ReturnsFailureWithoutActivating()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(ScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure("line 1: error: unknown command"));

            var rules = new[]
            {
                new SieveRule
                {
                    Name = "x",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions = { new SieveAction { Type = SieveActionType.Keep } }
                }
            };

            var result = await repo.SaveRulesAsync(Alice, rules);

            Assert.True(result.IsFailure);
            Assert.Equal("line 1: error: unknown command", result.Error);
            session.Verify(s => s.SetActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ----- SaveRawScriptAsync -----

        [Fact]
        public async Task SaveRawScriptAsync_PutsExactContentAndActivates()
        {
            const string raw = "require [\"fileinto\"];\nfileinto \"Inbox\";\n";
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(ScriptName, raw, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.SetActiveAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.SaveRawScriptAsync(Alice, raw);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.PutScriptAsync(ScriptName, raw, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveRawScriptAsync_WhenPutFails_PropagatesServerError()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.PutScriptAsync(ScriptName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure("syntax error at line 2"));

            var result = await repo.SaveRawScriptAsync(Alice, "garbage");

            Assert.True(result.IsFailure);
            Assert.Equal("syntax error at line 2", result.Error);
        }

        // ----- DeleteAllRulesAsync -----

        [Fact]
        public async Task DeleteAllRulesAsync_WhenScriptMissing_IsNoOp()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(Array.Empty<SieveScriptListEntry>()));

            var result = await repo.DeleteAllRulesAsync(Alice);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.SetActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(s => s.DeleteScriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DeleteAllRulesAsync_WhenScriptActive_DeactivatesThenDeletes()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry(ScriptName, true) }));
            session.Setup(s => s.SetActiveAsync(string.Empty, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());
            session.Setup(s => s.DeleteScriptAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.DeleteAllRulesAsync(Alice);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.SetActiveAsync(string.Empty, It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.DeleteScriptAsync(ScriptName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteAllRulesAsync_WhenScriptInactive_OnlyDeletes()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry(ScriptName, false) }));
            session.Setup(s => s.DeleteScriptAsync(ScriptName, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.DeleteAllRulesAsync(Alice);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.SetActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DeleteAllRulesAsync_WhenDeactivateFails_DoesNotDelete()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(new[] { new SieveScriptListEntry(ScriptName, true) }));
            session.Setup(s => s.SetActiveAsync(string.Empty, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure("denied"));

            var result = await repo.DeleteAllRulesAsync(Alice);

            Assert.True(result.IsFailure);
            session.Verify(s => s.DeleteScriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ----- Session is opened for the right target user -----

        [Fact]
        public async Task GetRuleSetAsync_OpensSessionForTargetUser()
        {
            var (repo, client, session) = CreateSut();
            session.Setup(s => s.ListScriptsAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<SieveScriptListEntry>>(Array.Empty<SieveScriptListEntry>()));

            await repo.GetRuleSetAsync(Alice);

            client.Verify(c => c.OpenSessionAsync("alice@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
