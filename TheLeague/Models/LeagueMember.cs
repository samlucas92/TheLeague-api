using TheLeague.Enums;

namespace TheLeague.Models;

public class LeagueMember
{
	public Guid Id { get; set; }
	public Guid LeagueId { get; set; }
	public Guid? UserId { get; set; }
	public string DisplayName { get; set; } = string.Empty;
	public string? EmailAddress { get; set; }
	public bool IsOfflineMember { get; set; }
	public LeagueMemberRole Role { get; set; }
	public LeagueMemberStatus Status { get; set; }
	public DateTime JoinedAt { get; set; }
	public DateTime? ApprovedAt { get; set; }
	public Guid? ApprovedByUserId { get; set; }
}
