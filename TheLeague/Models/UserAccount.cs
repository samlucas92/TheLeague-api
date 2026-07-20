namespace TheLeague.Models;

public class UserAccount
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string EmailAddress { get; set; } = string.Empty;
	public string PasswordHash { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
}
