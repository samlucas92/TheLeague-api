using TheLeague.Enums;

namespace TheLeague.Models;

public class League
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public Guid OwnerUserId { get; set; }
	public string JoinCode { get; set; } = string.Empty;
	public LeagueStatus Status { get; set; }
	public LeagueJoinMode JoinMode { get; set; }
	public LeaguePresetType PresetType { get; set; }
	public bool PublicViewEnabled { get; set; } = true;
	public DateTime? StartsAt { get; set; }
	public DateTime? EndsAt { get; set; }
	public int? MaximumParticipants { get; set; }
	public bool AllowMembersToLeave { get; set; }
	public bool ShowPendingPointsOnLeaderboard { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime? UpdatedAt { get; set; }
}
