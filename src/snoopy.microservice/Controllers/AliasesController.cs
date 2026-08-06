using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class AliasesController : ApiBaseController
{
    private readonly IAliasesRepository _userRepository;

    public AliasesController(IAliasesRepository userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// Add an aliases to the main mailbox.
    /// </summary>
    /// <param name="alias">the aliad to create</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">New alias has been successfully created</response>
    /// <response code="400">Bad request</response>
    /// <response code="401">Unauthenticated user</response>
    [Authorize]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ResultEnveloppe>> Add(Alias alias, CancellationToken cancellationToken)
    {
        Result result = await _userRepository.AddAliasAsync(AuthenticatedUser, alias, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Gets all aliases to the main mailbox
    /// </summary>
    /// <response code="200">Aliases list generated successfully</response>
    /// <response code="401">Unauthenticated user</response>
    [Authorize]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<Alias>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _userRepository.GetAliasesAsync(AuthenticatedUser, cancellationToken));
    }

    /// <summary>
    /// Deletes an alias to the main mailbox
    /// </summary>
    /// <param name="alias">alias to delete</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Alias successfully deleted</response>
    /// <response code="400">Bad request</response>
    /// <response code="401">Unauthenticated user</response>
    [Authorize]
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ResultEnveloppe>> Delete(Alias alias, CancellationToken cancellationToken)
    {
        Result result = await _userRepository.DeleteAliasAsync(AuthenticatedUser, alias, cancellationToken);
        return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
    }
}
