using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Authentication.Services
{
	public interface ITokenManager
	{
		AuthToken Generate(User user);
	}
}
