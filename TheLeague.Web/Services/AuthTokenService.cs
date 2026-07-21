using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using TheLeague.Models;

namespace TheLeague.Web.Services;

public class AuthTokenService(IConfiguration configuration)
{
	private readonly string signingKey = configuration["Auth:TokenSigningKey"]
		?? configuration["Mongo:ConnectionString"]
		?? "development-token-signing-key";

	public AuthenticatedUser ToAuthenticatedUser(UserAccount account) =>
		new(account.Id, account.Name, account.EmailAddress, CreateAccessToken(account.Id));

	public ClaimsPrincipal CreateClaimsPrincipal(Guid userId)
	{
		var claims = new[]
		{
			new Claim(ClaimTypes.NameIdentifier, userId.ToString())
		};
		var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
		return new ClaimsPrincipal(identity);
	}

	public string CreateAccessToken(Guid userId)
	{
		var expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
		var payload = $"{userId:N}.{expiresAt}";
		return $"{Base64UrlEncodeString(payload)}.{Sign(payload)}";
	}

	public bool TryValidateAccessToken(string accessToken, out Guid userId)
	{
		userId = Guid.Empty;
		var parts = accessToken.Split('.', 2);
		if (parts.Length != 2)
		{
			return false;
		}

		var payload = Base64UrlDecode(parts[0]);
		if (payload is null)
		{
			return false;
		}

		var expectedSignature = Sign(payload);
		if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(expectedSignature)))
		{
			return false;
		}

		var payloadParts = payload.Split('.', 2);
		return payloadParts.Length == 2 &&
			Guid.TryParseExact(payloadParts[0], "N", out userId) &&
			long.TryParse(payloadParts[1], out var expiresAt) &&
			DateTimeOffset.FromUnixTimeSeconds(expiresAt) > DateTimeOffset.UtcNow;
	}

	private string Sign(string payload)
	{
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
		return Base64UrlEncodeBytes(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
	}

	private static string Base64UrlEncodeString(string value) => Base64UrlEncodeBytes(Encoding.UTF8.GetBytes(value));

	private static string Base64UrlEncodeBytes(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static string? Base64UrlDecode(string value)
	{
		try
		{
			var padded = value.Replace('-', '+').Replace('_', '/');
			padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
			return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
		}
		catch (FormatException)
		{
			return null;
		}
	}
}
