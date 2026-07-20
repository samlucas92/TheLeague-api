using System.Security.Claims;

namespace TheLeague.Web.Extensions;

public static class UserExtensions
{
	public static Guid GetRequiredUserId(this ClaimsPrincipal user)
	{
		var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
		return Guid.TryParse(userId, out var parsed)
			? parsed
			: throw new UnauthorizedAccessException("Authenticated user id is missing.");
	}
}
