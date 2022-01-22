using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.MailAdminRestAPI.Models;
using weesky.MailAdminRestAPI.Repositories;

namespace weesky.MailAdminRestAPI.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	[Authorize]
	public class AliasesController : ApiBaseController
	{
		private readonly IAliasesRepository _userRepository;

		public AliasesController(IAliasesRepository userRepository)
		{
			_userRepository = userRepository;
		}

		/// <summary>
		/// Add an aliases to the main mailbox.
		/// </summary>
		/// <param name="alias">the aliad to add</param>
		/// <response code="204">new alias has been added</response>
		/// <response code="400">bad request</response>
		/// <response code="401">unauthenticated user</response>
		[Authorize]
		[HttpPost]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public ActionResult<ResultEnveloppe> Add(Alias alias)
		{
			Result result = _userRepository.AddAlias(AuthenticatedUser, alias);
			return FromResult(result, successStatusCode : StatusCodes.Status204NoContent);
		}

		/// <summary>
		/// Gets all aliases to the main mailbox
		/// </summary>
		/// <response code="200">successful action</response>
		/// <response code="401">unauthenticated user</response>
		[Authorize]
		[HttpGet]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public ActionResult<IEnumerable<Alias>> List()
		{
			return Ok(_userRepository.GetAliases(AuthenticatedUser));
		}

		/// <summary>
		/// Deletes an alias to the main mailbox
		/// </summary>
		/// <param name="alias">the alias to delete</param>
		/// <response code="200">successful action</response>
		/// <response code="400">Bad request</response>
		/// <response code="401">unauthenticated user</response>
		[Authorize]
		[HttpDelete]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<ResultEnveloppe> Delete(Alias alias)
		{
			Result result = _userRepository.DeleteAlias(AuthenticatedUser, alias);
			return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
		}
	}
}
