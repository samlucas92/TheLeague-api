using TheLeague.Enums;

namespace TheLeague.Models;

public class LeagueInvitation
{
	public Guid Id { get; set; }
	public Guid LeagueId { get; set; }
	public string EmailAddress { get; set; } = string.Empty;
	public string SuggestedDisplayName { get; set; } = string.Empty;
	public LeagueMemberRole Role { get; set; }
	public Guid InvitedByUserId { get; set; }
	public string Status { get; set; } = "Pending";
	public DateTime CreatedAt { get; set; }
	public DateTime? ExpiresAt { get; set; }
}
