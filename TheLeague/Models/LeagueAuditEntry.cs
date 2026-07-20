using TheLeague.Enums;

namespace TheLeague.Models;

public class LeagueAuditEntry
{
	public Guid Id { get; set; }
	public Guid LeagueId { get; set; }
	public LeagueAuditAction Action { get; set; }
	public Guid PerformedByUserId { get; set; }
	public string EntityType { get; set; } = string.Empty;
	public Guid? EntityId { get; set; }
	public string Summary { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
}
