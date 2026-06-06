using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RulesController : ApiBaseController
    {
        private readonly ISieveRepository _sieveRepository;

        public RulesController(ISieveRepository sieveRepository)
        {
            _sieveRepository = sieveRepository;
        }

        /// <summary>
        /// Returns the authenticated user's Sieve configuration: structured rules when the
        /// script was produced by this UI, or the raw script when it was hand-edited.
        /// </summary>
        /// <response code="200">Rule set retrieved</response>
        /// <response code="400">Rules service is unavailable or rejected the request</response>
        /// <response code="401">Unauthenticated user</response>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<SieveRuleSet>> Get(CancellationToken cancellationToken)
        {
            Result<SieveRuleSet> result = await _sieveRepository.GetRuleSetAsync(AuthenticatedUser, cancellationToken);
            if (result.IsSuccess) return Ok(result.Value);
            return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
        }

        /// <summary>
        /// Replaces all of the authenticated user's structured rules.
        /// Switches the script back to structured mode if it had been edited as raw Sieve.
        /// </summary>
        /// <param name="rules">The complete, ordered list of rules.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <response code="204">Rules saved and activated</response>
        /// <response code="400">A rule is invalid or the server rejected the resulting Sieve script</response>
        /// <response code="401">Unauthenticated user</response>
        [HttpPut]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<ResultEnveloppe>> Replace([FromBody] List<SieveRule> rules, CancellationToken cancellationToken)
        {
            if (rules == null)
                return BadRequest(ResultEnveloppe.CrateErrorEnveloppe("Request body is required"));

            Result result = await _sieveRepository.SaveRulesAsync(AuthenticatedUser, rules, cancellationToken);
            return FromResultWithEnveloppe(result, successStatusCode: StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// Deletes the managed Sieve script (deactivating it first). The user retains
        /// any other scripts they uploaded out-of-band.
        /// </summary>
        /// <response code="204">Managed script removed (or absent to begin with)</response>
        /// <response code="400">Rules service unavailable</response>
        /// <response code="401">Unauthenticated user</response>
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<ResultEnveloppe>> DeleteAll(CancellationToken cancellationToken)
        {
            Result result = await _sieveRepository.DeleteAllRulesAsync(AuthenticatedUser, cancellationToken);
            return FromResultWithEnveloppe(result, successStatusCode: StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// Returns the raw Sieve text currently stored on the server. Used by the advanced editor.
        /// </summary>
        /// <response code="200">Raw script retrieved (empty if no script exists yet)</response>
        /// <response code="400">Rules service unavailable</response>
        /// <response code="401">Unauthenticated user</response>
        [HttpGet("Raw")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<SieveRawScript>> GetRaw(CancellationToken cancellationToken)
        {
            Result<string> result = await _sieveRepository.GetRawScriptAsync(AuthenticatedUser, cancellationToken);
            if (result.IsSuccess) return Ok(new SieveRawScript { Content = result.Value });
            return BadRequest(ResultEnveloppe.CrateErrorEnveloppe(result.Error));
        }

        /// <summary>
        /// Replaces the managed Sieve script with raw Sieve text typed by the user in
        /// the advanced editor. The structured representation is lost (the marker is
        /// not added back) until the user reverts via <c>PUT /api/Rules</c>.
        /// </summary>
        /// <param name="script">The raw script content.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <response code="204">Script saved and activated</response>
        /// <response code="400">Server rejected the script (Sieve compilation error)</response>
        /// <response code="401">Unauthenticated user</response>
        [HttpPut("Raw")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<ResultEnveloppe>> PutRaw([FromBody] SieveRawScript script, CancellationToken cancellationToken)
        {
            if (script == null)
                return BadRequest(ResultEnveloppe.CrateErrorEnveloppe("Request body is required"));

            Result result = await _sieveRepository.SaveRawScriptAsync(AuthenticatedUser, script.Content ?? string.Empty, cancellationToken);
            return FromResultWithEnveloppe(result, successStatusCode: StatusCodes.Status204NoContent);
        }
    }
}
