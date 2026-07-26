using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TheLeague.Models;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Security;

public interface IJwtTokenService
{
	LoginResponse CreateLoginResponse(UserAccount account);
}

public sealed class JwtTokenService(IOptions<JwtSettings> jwtSettings) : IJwtTokenService
{
	private readonly JwtSettings jwtSettings = jwtSettings.Value;

	public LoginResponse CreateLoginResponse(UserAccount account)
	{
		var expiresAt = DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes);
		var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
		var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
		var claims = new List<Claim>
		{
			new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
			new(JwtRegisteredClaimNames.Email, account.EmailAddress),
			new(ClaimTypes.NameIdentifier, account.Id.ToString()),
			new(ClaimTypes.Email, account.EmailAddress)
		};

		var token = new JwtSecurityToken(
			issuer: jwtSettings.Issuer,
			audience: jwtSettings.Audience,
			claims: claims,
			expires: expiresAt,
			signingCredentials: credentials);

		return new LoginResponse
		{
			Token = new JwtSecurityTokenHandler().WriteToken(token),
			ExpiresAt = expiresAt,
			User = new AuthenticatedUser(account.Id, account.Name, account.EmailAddress, account.IsEmailVerified)
		};
	}
}
