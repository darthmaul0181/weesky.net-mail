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
	public class AccountController : ApiBaseController
	{
		private readonly IUsersRepository _usersRepository;
		
		public AccountController(IUsersRepository usersRepository)
		{
			_usersRepository = usersRepository;
		}

		/// <summary>
		/// Change the mailbox password
		/// </summary>
		/// <param name="secretChange">the new secret</param>
		/// <response code="204">Secret changed successfully</response>
		/// <response code="400">Wrong credentials</response>
		/// <response code="401">Unauthenticated user</response>
		[HttpPatch("ChangeSecret")]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public ActionResult ChangePassword(SecretChange secretChange)
		{
			Result result = _usersRepository.ChangePassword(AuthenticatedUser, secretChange.NewPassword, secretChange.OldPassword);
			return FromResult(result, successStatusCode: StatusCodes.Status204NoContent);
		}
	}
}
