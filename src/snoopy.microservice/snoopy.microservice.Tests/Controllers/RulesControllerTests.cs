using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.RuleProviders.Rainloop;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class RulesControllerTests
{
    private const string MasterUser = "master";
    private const string MasterPassword = "master-secret";
    private const string SieveHost = "sieve.home.test";

    private readonly Mock<ISieveRepository> _repo = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();
    private readonly IRuleProviderRegistry _registry = new RuleProviderRegistry(
        new IRuleProvider[] { new WeeskyRuleProvider(), new RainloopRuleProvider() });

    private readonly SieveOptions _sieveOptions = new()
    {
        Host = SieveHost,
        Port = 4190,
        MasterUser = MasterUser,
        MasterPassword = MasterPassword
    };

    private RulesController CreateController()
    {
        ResolveTo(TestConnections.Primary("alice@weesky.be", "hunter2"));
        return new RulesController(_repo.Object, _registry, _connections.Object, Options.Create(_sieveOptions))
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be")
        };
    }

    /// <summary>Moq resolves overlapping setups by recency: call after <c>CreateController()</c>.</summary>
    private void ResolveTo(MailAccountConnection connection)
        => _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Result.Success(connection));

    private void FailResolution(string error)
        => _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Result.Failure<MailAccountConnection>(error));

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
        _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success(ruleSet));

        var result = await CreateController().Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(ruleSet, ok.Value);
    }

    /// <summary>
    /// Every way the rules service can let us down, and none of them is the caller's mistake: a
    /// silent peer and an oversized response are outages exactly as a refused connection is.
    /// </summary>
    [Theory]
    [InlineData(SieveErrors.Unreachable)]
    [InlineData(SieveErrors.NotConfigured)]
    [InlineData(SieveErrors.AuthenticationFailed)]
    [InlineData(SieveErrors.NotSecure)]
    [InlineData(SieveErrors.TimedOut)]
    [InlineData(SieveErrors.ResponseTooLarge)]
    public async Task Get_WhenTheRulesServiceIsDown_Returns502WithErrorEnvelope(string error)
    {
        _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Failure<SieveRuleSet>(error));

        var result = await CreateController().Get(CancellationToken.None);

        var bad = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, bad.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(bad.Value);
        Assert.Equal(error, envelope.Message);
    }

    // ----- PUT /api/Rules -----

    [Fact]
    public async Task Replace_ForwardsProviderAndScriptName()
    {
        _repo.Setup(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
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
        _repo.Verify(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                           "rainloop", "rainloop.user", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Replace_WithNullProvider_ForwardsAsNull()
    {
        _repo.Setup(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                          null, null, It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success());

        await CreateController().Replace(new SaveRulesRequest(), CancellationToken.None);

        _repo.Verify(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                           null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Replace_WhenRepoFails_Returns400()
    {
        _repo.Setup(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
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
        _repo.Verify(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                                           It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- DELETE /api/Rules -----

    [Fact]
    public async Task DeleteAll_WhenRepoSucceeds_Returns204()
    {
        _repo.Setup(r => r.DeleteAllRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
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
        _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
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
        _repo.Setup(r => r.SaveRawScriptAsync(It.IsAny<SieveConnection>(), It.IsAny<string>(), "rainloop.user", It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success());

        var result = await CreateController().PutRaw(new SieveRawScript { Content = "keep;", ScriptName = "rainloop.user" }, CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(204, status.StatusCode);
        _repo.Verify(r => r.SaveRawScriptAsync(It.IsAny<SieveConnection>(), "keep;", "rainloop.user", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PutRaw_NullBody_Returns400()
    {
        var result = await CreateController().PutRaw(null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _repo.Verify(r => r.SaveRawScriptAsync(It.IsAny<SieveConnection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- POST /api/Rules/CompatibilityCheck -----

    [Fact]
    public void CompatibilityCheck_AllRepresentable_ReturnsCompatible()
    {
        var body = new CompatibilityCheckRequest
        {
            ProviderId = "rainloop",
            Rules =
            {
                new SieveRule
                {
                    Name = "ok",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions = { new SieveAction { Type = SieveActionType.FileInto, Argument = "X" } }
                }
            }
        };

        var result = CreateController().CompatibilityCheck(body);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<CompatibilityCheckResult>(ok.Value);
        Assert.True(payload.Compatible);
        Assert.Empty(payload.Incompatible);
    }

    [Fact]
    public void CompatibilityCheck_IncompatibleRule_ListsItWithReason()
    {
        var id = Guid.NewGuid();
        var body = new CompatibilityCheckRequest
        {
            ProviderId = "rainloop",
            Rules =
            {
                new SieveRule
                {
                    Id = id,
                    Name = "extended",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions =
                    {
                        new SieveAction { Type = SieveActionType.FileInto, Argument = "A" },
                        new SieveAction { Type = SieveActionType.Redirect, Argument = "y@z.com" }
                    }
                }
            }
        };

        var result = CreateController().CompatibilityCheck(body);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<CompatibilityCheckResult>(ok.Value);
        Assert.False(payload.Compatible);
        var entry = Assert.Single(payload.Incompatible);
        Assert.Equal(id, entry.Id);
        Assert.Equal("extended", entry.Name);
        Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
    }

    [Fact]
    public void CompatibilityCheck_WeeskyTarget_AlwaysCompatible()
    {
        var body = new CompatibilityCheckRequest
        {
            ProviderId = "weesky",
            Rules =
            {
                new SieveRule
                {
                    Name = "extended",
                    Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Contains, Value = "x" } },
                    Actions =
                    {
                        new SieveAction { Type = SieveActionType.SetFlag, Argument = @"\Flagged" },
                        new SieveAction { Type = SieveActionType.FileInto, Argument = "A" }
                    }
                }
            }
        };

        var result = CreateController().CompatibilityCheck(body);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<CompatibilityCheckResult>(ok.Value);
        Assert.True(payload.Compatible);
    }

    [Fact]
    public void CompatibilityCheck_UnknownProvider_Returns400()
    {
        var result = CreateController().CompatibilityCheck(new CompatibilityCheckRequest { ProviderId = "nope" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void CompatibilityCheck_NullBody_Returns400()
    {
        var result = CreateController().CompatibilityCheck(null!);

        Assert.IsType<BadRequestObjectResult>(result.Result);
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

    // ----- DELETE /api/Rules (failure) -----

    [Fact]
    public async Task DeleteAll_WhenRepoFails_Returns400()
    {
        _repo.Setup(r => r.DeleteAllRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Failure("deletion error"));

        var result = await CreateController().DeleteAll(CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("deletion error", envelope.Message);
    }

    // ----- GET /api/Rules/Raw (failure) -----

    [Fact]
    public async Task GetRaw_WhenTheRulesServiceIsDown_Returns502()
    {
        _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Failure<SieveRuleSet>(SieveErrors.Unreachable));

        var result = await CreateController().GetRaw(CancellationToken.None);

        var bad = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, bad.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(bad.Value);
        Assert.Equal(SieveErrors.Unreachable, envelope.Message);
    }

    // The other half of the split: a request the caller really did get wrong stays a 400.
    [Fact]
    public async Task Get_WhenTheScriptCannotBeParsed_StaysA400()
    {
        _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Failure<SieveRuleSet>("Unknown rule provider: nope"));

        var result = await CreateController().Get(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Replace_WhenTheRulesServiceIsDown_Returns502()
    {
        _repo.Setup(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Failure(SieveErrors.NotConfigured));

        var result = await CreateController().Replace(new SaveRulesRequest { Rules = [] }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, obj.StatusCode);
    }

    // ----- PUT /api/Rules/Raw (failure) -----

    [Fact]
    public async Task PutRaw_WhenRepoFails_Returns400()
    {
        _repo.Setup(r => r.SaveRawScriptAsync(It.IsAny<SieveConnection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Failure("write error"));

        var result = await CreateController().PutRaw(new SieveRawScript { Content = "keep;", ScriptName = "test" }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("write error", envelope.Message);
    }

    // ----- The active account -----
    // Two authentication shapes, and crossing them is the defect this whole task exists to avoid.
    // Master impersonation is the primary account's alone; every connected mailbox is entered with
    // the credentials we hold for it, so a password change revokes its filters along with its mail.

    [Fact]
    public async Task Get_OnThePrimaryAccount_ImpersonatesTheMailboxWithTheMasterCredentials()
    {
        SucceedGet();

        await CreateController().Get(CancellationToken.None);

        _repo.Verify(r => r.GetRuleSetAsync(
            new SieveConnection(SieveHost, 4190, "alice@weesky.be", MasterUser, MasterPassword),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // A shared mailbox on our own server: its own login against the house endpoint, never the
    // master account — that let a user keep writing filters after losing the mailbox's password.
    [Fact]
    public async Task Get_OnALocalConnectedAccount_AuthenticatesWithItsOwnCredentials()
    {
        SucceedGet();
        var controller = CreateController();
        ResolveTo(TestConnections.ConnectedLocal(Guid.NewGuid().ToString(), "team@weesky.be", "shared-secret"));

        await controller.Get(CancellationToken.None);

        _repo.Verify(r => r.GetRuleSetAsync(
            new SieveConnection(SieveHost, 4190, string.Empty, "team@weesky.be", "shared-secret"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Our own server always speaks Sieve, so a local mailbox carrying no Sieve endpoint of its own
    // must take the house one rather than fall through to the external branch's 404.
    [Fact]
    public async Task Get_OnALocalConnectedAccount_NeverAnswersSieveUnsupported()
    {
        SucceedGet();
        var controller = CreateController();
        ResolveTo(TestConnections.ConnectedLocal(Guid.NewGuid().ToString(), "team@weesky.be", "shared-secret"));

        var result = await controller.Get(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    // No master account exists on somebody else's server: we authenticate as the mailbox itself,
    // and the authorization identity must stay empty or the server refuses the session.
    [Fact]
    public async Task Get_OnAnExternalAccountWithSieve_AuthenticatesWithItsOwnCredentials()
    {
        SucceedGet();
        var controller = CreateController();
        ResolveTo(TestConnections.ConnectedWithSieve(Guid.NewGuid().ToString(), "bob@external.test", "bob-secret"));

        await controller.Get(CancellationToken.None);

        _repo.Verify(r => r.GetRuleSetAsync(
            new SieveConnection("sieve.external.test", 4190, string.Empty, "bob@external.test", "bob-secret"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_OnAnExternalAccountWithoutSieve_Returns404SieveUnsupported()
    {
        var controller = CreateController();
        ResolveTo(TestConnections.Connected(Guid.NewGuid().ToString(), "bob@external.test", "bob-secret"));

        var result = await controller.Get(CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(SieveErrors.Unsupported, Assert.IsType<ResultEnveloppe>(notFound.Value).Message);
        _repo.Verify(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("credentials_unavailable", typeof(UnauthorizedObjectResult))]
    [InlineData(ConnectedAccountErrors.AccountNotFound, typeof(NotFoundObjectResult))]
    [InlineData(ConnectedAccountErrors.CredentialsInvalid, typeof(ConflictObjectResult))]
    public async Task Get_WhenTheAccountCannotBeResolved_MapsTheFailureLikeMailController(string error, Type expected)
    {
        var controller = CreateController();
        FailResolution(error);

        var result = await controller.Get(CancellationToken.None);

        Assert.IsType(expected, result.Result);
        var envelope = Assert.IsType<ResultEnveloppe>(Assert.IsAssignableFrom<ObjectResult>(result.Result).Value);
        Assert.Equal(error, envelope.Message);
    }

    // The guard is on every action that reaches ManageSieve, not only the read.

    [Fact]
    public async Task Replace_OnAnExternalAccountWithoutSieve_Returns404()
    {
        var controller = CreateController();
        ResolveTo(TestConnections.Connected(Guid.NewGuid().ToString(), "bob@external.test", "bob-secret"));

        var result = await controller.Replace(new SaveRulesRequest { Rules = [] }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _repo.Verify(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAll_OnAnExternalAccountWithoutSieve_Returns404()
    {
        var controller = CreateController();
        ResolveTo(TestConnections.Connected(Guid.NewGuid().ToString(), "bob@external.test", "bob-secret"));

        var result = await controller.DeleteAll(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _repo.Verify(r => r.DeleteAllRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRaw_OnAnExternalAccountWithoutSieve_Returns404()
    {
        var controller = CreateController();
        ResolveTo(TestConnections.Connected(Guid.NewGuid().ToString(), "bob@external.test", "bob-secret"));

        var result = await controller.GetRaw(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _repo.Verify(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PutRaw_OnAnExternalAccountWithSieve_UsesItsOwnCredentials()
    {
        _repo.Setup(r => r.SaveRawScriptAsync(It.IsAny<SieveConnection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success());
        var controller = CreateController();
        ResolveTo(TestConnections.ConnectedWithSieve(Guid.NewGuid().ToString(), "bob@external.test", "bob-secret"));

        await controller.PutRaw(new SieveRawScript { Content = "stop;" }, CancellationToken.None);

        _repo.Verify(r => r.SaveRawScriptAsync(
            new SieveConnection("sieve.external.test", 4190, string.Empty, "bob@external.test", "bob-secret"),
            "stop;", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Without a master password the impersonation would go out with a blank credential: a stream of
    // failed master logins against our own Dovecot, and a 502 blaming the server for our own gap.
    [Fact]
    public async Task Get_OnThePrimaryAccountWithoutAMasterPassword_Returns502WithoutOpeningASession()
    {
        SucceedGet();
        _sieveOptions.MasterPassword = string.Empty;

        var result = await CreateController().Get(CancellationToken.None);

        var bad = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, bad.StatusCode);
        Assert.Equal(SieveErrors.NotConfigured, Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _repo.Verify(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_OnThePrimaryAccountWithoutAMasterPassword_NeverOpensASession()
    {
        _sieveOptions.MasterPassword = "   ";

        var result = await CreateController().Replace(new SaveRulesRequest { Rules = [] }, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        _repo.Verify(r => r.SaveRulesAsync(It.IsAny<SieveConnection>(), It.IsAny<IReadOnlyList<SieveRule>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The two provider endpoints answer out of the registry alone — no mailbox, no session — so
    // they stay reachable for an account whose server has no Sieve at all.
    [Fact]
    public void ListProviders_NeverResolvesAnAccount()
    {
        CreateController().ListProviders();

        _connections.Verify(c => c.ResolveAsync(
            It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CompatibilityCheck_NeverResolvesAnAccount()
    {
        CreateController().CompatibilityCheck(new CompatibilityCheckRequest { ProviderId = "rainloop" });

        _connections.Verify(c => c.ResolveAsync(
            It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SucceedGet() =>
        _repo.Setup(r => r.GetRuleSetAsync(It.IsAny<SieveConnection>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success(new SieveRuleSet { Kind = SieveScriptKind.Structured }));
}
