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
		/// <returns></returns>
		[HttpPatch("ChangeSecret")]
		public ActionResult<ResultEnveloppe> ChangePassword(SecretChange secretChange)
		{
			Result result = _usersRepository.ChangePassword(AuthenticatedUser, secretChange.NewPassword, secretChange.OldPassword);
			return FromResult(result);
		}
	}
}
