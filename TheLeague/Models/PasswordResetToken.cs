namespace TheLeague.Models;

public class PasswordResetToken
{
	public Guid Id { get; set; }
	public Guid UserId { get; set; }
	public string TokenHash { get; set; } = string.Empty;
	public DateTime ExpiresAt { get; set; }
	public DateTime? UsedAt { get; set; }
	public DateTime CreatedAt { get; set; }
}
