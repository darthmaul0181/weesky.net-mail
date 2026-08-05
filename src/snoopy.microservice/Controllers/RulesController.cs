using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class RulesController(
    ISieveRepository sieveRepository,
    IRuleProviderRegistry providers,
    IAccountConnectionResolver connections,
    IOptions<SieveOptions> sieveOptions) : ApiBaseController
{
    /// <summary>
    /// The ManageSieve target for the active account, or the error to answer with. The only place
    /// a <see cref="SieveConnection"/> is built, and the only place the SASL shape is chosen. Master
    /// impersonation is the primary account's alone; a connected mailbox authenticates with the
    /// credentials we hold for it, so revoking its password revokes its filters in the same move.
    /// </summary>
    private async Task<AccountResolution<SieveConnection>> TryResolveAsync(
        CancellationToken cancellationToken)
    {
        var resolved = await connections.ResolveAsync(AuthenticatedUser, Request, cancellationToken);
        if (resolved.IsFailure)
            return AccountResolution<SieveConnection>.Failure(ConnectedAccountError(resolved.Error));

        var account = resolved.Value;
        var sieve = sieveOptions.Value;

        // ManageSieve here is SASL PLAIN, hand-assembled: an access token is not a password, and
        // no provider reached over OAuth offers ManageSieve anyway.
        if (account.Credential is not PasswordCredential mailbox)
            return AccountResolution<SieveConnection>.Failure(NotFoundEnveloppe(SieveErrors.Unsupported));

        if (account.AccountId == MailAccountConnection.Primary)
        {
            // A blank master password would still open a session and offer the mailbox with an
            // empty credential — a stream of failed master logins against our own Dovecot, and a
            // 502 blaming the server. Fail here instead. Host and MasterUser the client guards.
            if (string.IsNullOrWhiteSpace(sieve.MasterPassword))
                return AccountResolution<SieveConnection>.Failure(SieveFailure(SieveErrors.NotConfigured));

            return AccountResolution<SieveConnection>.Success(new SieveConnection(
                sieve.Host, sieve.Port, account.Username, sieve.MasterUser, sieve.MasterPassword));
        }

        // A connected mailbox on our own server: its own login, but the house endpoint — the
        // resolver leaves SieveHost null for home connections, since there is nothing to store.
        if (account.IsHomeServer)
            return AccountResolution<SieveConnection>.Success(new SieveConnection(
                sieve.Host, sieve.Port, string.Empty, account.Username, mailbox.Password));

        if (account.SieveHost == null || account.SievePort == null)
            return AccountResolution<SieveConnection>.Failure(NotFoundEnveloppe(SieveErrors.Unsupported));

        return AccountResolution<SieveConnection>.Success(new SieveConnection(
            account.SieveHost, account.SievePort.Value, string.Empty, account.Username, mailbox.Password));
    }

    /// <summary>
    /// A ManageSieve outage is the service's fault, not the caller's: 502, the same split
    /// the api/Mail controllers apply to IMAP. Anything else — an unknown provider, a rule the format
    /// cannot express — really is a bad request.
    /// </summary>
    private ActionResult SieveFailure(string error) =>
        SieveErrors.IsServiceFailure(error) ? BadGatewayEnveloppe(error) : BadRequestEnveloppe(error);

    private static int SieveErrorStatus(Result result) =>
        result.IsFailure && SieveErrors.IsServiceFailure(result.Error)
            ? StatusCodes.Status502BadGateway
            : StatusCodes.Status400BadRequest;

    /// <summary>
    /// Returns the active account's Sieve configuration: structured rules when a
    /// registered provider can decode the script, or the raw script when none matches.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="404">No such account, or its domain has no Sieve endpoint</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SieveRuleSet>> Get(CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        Result<SieveRuleSet> result = await sieveRepository.GetRuleSetAsync(connection, cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return SieveFailure(result.Error);
    }

    /// <summary>
    /// Replaces all of the active account's structured rules. The body may specify
    /// which provider to compile with and which script to write to; otherwise the
    /// default provider and its default script name are used.
    /// </summary>
    /// <param name="request">Rules + optional provider/script hints.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="404">No such account, or its domain has no Sieve endpoint</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ResultEnveloppe>> Replace([FromBody] SaveRulesRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
            return BadRequestEnveloppe("Request body is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        Result result = await sieveRepository.SaveRulesAsync(
            connection, request.Rules ?? new List<SieveRule>(), request.ProviderId, request.ScriptName, cancellationToken);
        return FromResult(result, SieveErrorStatus(result), StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Deletes the active managed Sieve script (deactivating it first).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="404">No such account, or its domain has no Sieve endpoint</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ResultEnveloppe>> DeleteAll(CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        Result result = await sieveRepository.DeleteAllRulesAsync(connection, cancellationToken);
        return FromResult(result, SieveErrorStatus(result), StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Returns the raw Sieve text currently stored on the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="404">No such account, or its domain has no Sieve endpoint</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpGet("Raw")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SieveRawScript>> GetRaw(CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        Result<SieveRuleSet> result = await sieveRepository.GetRuleSetAsync(connection, cancellationToken);
        if (result.IsFailure)
            return SieveFailure(result.Error);

        return Ok(new SieveRawScript
        {
            Content = result.Value.RawScript,
            ScriptName = result.Value.ScriptName
        });
    }

    /// <summary>
    /// Replaces the script with raw Sieve text. The structured representation is lost
    /// until the user issues a fresh structured PUT.
    /// </summary>
    /// <param name="script">The raw script content and optional script name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="404">No such account, or its domain has no Sieve endpoint</response>
    /// <response code="409">The connected account's stored credentials no longer decrypt</response>
    [HttpPut("Raw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ResultEnveloppe>> PutRaw([FromBody] SieveRawScript script, CancellationToken cancellationToken)
    {
        if (script == null)
            return BadRequestEnveloppe("Request body is required");

        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        Result result = await sieveRepository.SaveRawScriptAsync(
            connection, script.Content ?? string.Empty, script.ScriptName, cancellationToken);
        return FromResult(result, SieveErrorStatus(result), StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Checks whether the supplied rules can be represented by a target provider's format,
    /// without writing anything. The frontend calls this before switching providers (e.g.
    /// turning off "Extended rules") to preview which rules would be lost in the conversion.
    /// </summary>
    /// <param name="request">Target provider id + the rules to test.</param>
    [HttpPost("CompatibilityCheck")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CompatibilityCheckResult> CompatibilityCheck([FromBody] CompatibilityCheckRequest request)
    {
        if (request == null)
            return BadRequestEnveloppe("Request body is required");

        var provider = request.ProviderId != null
            ? providers.GetById(request.ProviderId)
            : providers.Default;
        if (provider == null)
            return BadRequestEnveloppe($"Unknown rule provider: {request.ProviderId}");

        var incompatible = new List<IncompatibleRule>();
        foreach (var rule in request.Rules ?? new List<SieveRule>())
        {
            var check = provider.CanRepresent(rule);
            if (check.IsFailure)
                incompatible.Add(new IncompatibleRule { Id = rule.Id, Name = rule.Name, Reason = check.Error });
        }

        return Ok(new CompatibilityCheckResult
        {
            Compatible = incompatible.Count == 0,
            Incompatible = incompatible
        });
    }

    /// <summary>
    /// Lists the rule providers supported by the server (e.g. weesky, rainloop) so the
    /// frontend can offer format selection.
    /// </summary>
    [HttpGet("Providers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IEnumerable<RuleProviderInfo>> ListProviders()
    {
        var defaultId = providers.Default.Id;
        var infos = providers.All
            .Select(p => new RuleProviderInfo
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                DefaultScriptName = p.DefaultScriptName,
                IsDefault = p.Id == defaultId
            })
            .ToList();
        return Ok(infos);
    }
}
