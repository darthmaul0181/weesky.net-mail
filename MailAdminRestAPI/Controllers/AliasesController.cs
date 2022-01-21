using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.MailAdminRestAPI.Authentication.Extensions;
using weesky.MailAdminRestAPI.Data;
using weesky.MailAdminRestAPI.Models;
using weesky.MailAdminRestAPI.Services;

namespace weesky.MailAdminRestAPI.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	[Authorize]
	public class AliasesController : ControllerBase
	{
		private readonly IRepository _userRepository;

		public AliasesController(IRepository userRepository)
		{
			_userRepository = userRepository;
		}

		/// <summary>
		/// Add an aliases to the main mailbox.
		/// </summary>
		/// <param name="alias">the aliad to add</param>
		/// <response code="200">successful action</response>
		/// <response code="400">bad request</response>
		/// <response code="401">unauthenticated user</response>
		[Authorize]
		[HttpPost]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public ActionResult<RepositoryResult> Add(Alias alias)
		{
			RepositoryResult result = _userRepository.AddAlias(this.GetUser(), alias);

			return StatusCode(result.State == RespositoryResultState.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest, result);
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
			return Ok(_userRepository.GetAliases(this.GetUser()));
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
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<RepositoryResult> Delete(Alias alias)
		{
			RepositoryResult result = _userRepository.DeleteAlias(this.GetUser(), alias);

			return StatusCode(result.State == RespositoryResultState.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest, result);
		}
	}
}
