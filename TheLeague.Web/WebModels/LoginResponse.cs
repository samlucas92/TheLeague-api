using TheLeague.Models;

namespace TheLeague.Web.WebModels;

public sealed class LoginResponse
{
	public string Token { get; set; } = string.Empty;

	public DateTime ExpiresAt { get; set; }

	public AuthenticatedUser User { get; set; } = null!;

	public Guid Id => User.Id;

	public string Name => User.Name;

	public string EmailAddress => User.EmailAddress;

	public string AccessToken => Token;
}
