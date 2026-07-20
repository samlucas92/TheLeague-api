using TheLeague.Enums;

namespace TheLeague.Models;

public class PointAllocation
{
	public Guid Id { get; set; }
	public Guid LeagueId { get; set; }
	public Guid LeagueMemberId { get; set; }
	public Guid? ChallengeId { get; set; }
	public Guid? SubmissionId { get; set; }
	public int Points { get; set; }
	public string Reason { get; set; } = string.Empty;
	public PointAllocationSource Source { get; set; }
	public Guid AwardedByUserId { get; set; }
	public Guid? ReversesAllocationId { get; set; }
	public Guid? RelatedAllocationId { get; set; }
	public DateTime AwardedAt { get; set; }
}
