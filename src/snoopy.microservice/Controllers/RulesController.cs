using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.RuleProviders;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class RulesController : ApiBaseController
{
    private readonly ISieveRepository _sieveRepository;
    private readonly IRuleProviderRegistry _providers;

    public RulesController(ISieveRepository sieveRepository, IRuleProviderRegistry providers)
    {
        _sieveRepository = sieveRepository;
        _providers = providers;
    }

    /// <summary>
    /// Returns the authenticated user's Sieve configuration: structured rules when a
    /// registered provider can decode the script, or the raw script when none matches.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SieveRuleSet>> Get(CancellationToken cancellationToken)
    {
        Result<SieveRuleSet> result = await _sieveRepository.GetRuleSetAsync(AuthenticatedUser, cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(result.Error));
    }

    /// <summary>
    /// Replaces all of the authenticated user's structured rules. The body may specify
    /// which provider to compile with and which script to write to; otherwise the
    /// default provider and its default script name are used.
    /// </summary>
    /// <param name="request">Rules + optional provider/script hints.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ResultEnveloppe>> Replace([FromBody] SaveRulesRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        Result result = await _sieveRepository.SaveRulesAsync(
            AuthenticatedUser, request.Rules ?? new List<SieveRule>(), request.ProviderId, request.ScriptName, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Deletes the active managed Sieve script (deactivating it first).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ResultEnveloppe>> DeleteAll(CancellationToken cancellationToken)
    {
        Result result = await _sieveRepository.DeleteAllRulesAsync(AuthenticatedUser, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Returns the raw Sieve text currently stored on the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("Raw")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SieveRawScript>> GetRaw(CancellationToken cancellationToken)
    {
        Result<SieveRuleSet> result = await _sieveRepository.GetRuleSetAsync(AuthenticatedUser, cancellationToken);
        if (result.IsFailure)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(result.Error));

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
    [HttpPut("Raw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ResultEnveloppe>> PutRaw([FromBody] SieveRawScript script, CancellationToken cancellationToken)
    {
        if (script == null)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        Result result = await _sieveRepository.SaveRawScriptAsync(
            AuthenticatedUser, script.Content ?? string.Empty, script.ScriptName, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
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
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        var provider = request.ProviderId != null
            ? _providers.GetById(request.ProviderId)
            : _providers.Default;
        if (provider == null)
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe($"Unknown rule provider: {request.ProviderId}"));

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
        var defaultId = _providers.Default.Id;
        var infos = _providers.All
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
