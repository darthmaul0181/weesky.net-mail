using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication.Extensions;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Authentication.Services
{
	public class TokenManager : ITokenManager
	{
		private IOptions<TokenConstants> TokenConstants { get;}

		public TokenManager(IOptions<TokenConstants> tokenConstants)
		{
			TokenConstants = tokenConstants;
		}

		public AuthToken Generate(User user)
		{
			var tokenBuilder = new TokenBuilder();

			JwtSecurityToken token = tokenBuilder.AddClaim(ClaimTypes.Upn, user.Name)
				.AddClaim(ClaimTypes.Dns, user.Domain)
				.AddIssuer(TokenConstants.Value.Issuer)
				.AddAudience(TokenConstants.Value.Audience)
				.AddExpiry(TokenConstants.Value.ExpiryInMinutes)
				.AddKey(TokenConstants.Value.Key)
				.Build();

			return new AuthToken
			{
				ExpiresIn = TokenConstants.Value.ExpiryInMinutes,
				Token = token.SerializeToString()
			};
		}
	}
}
