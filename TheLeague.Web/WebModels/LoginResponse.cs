using TheLeague.Models;

namespace TheLeague.Web.WebModels;

public sealed class LoginResponse
{
	public string Token { get; set; } = string.Empty;

	public DateTime ExpiresAt { get; set; }

	public AuthenticatedUser User { get; set; } = null!;
}
