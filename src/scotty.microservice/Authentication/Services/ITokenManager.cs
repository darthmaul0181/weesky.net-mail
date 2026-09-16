using weesky.Scotty.Microservice.Authentication.Models;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Authentication.Services;

public interface ITokenManager
{
    AuthToken Generate(User user);
}
