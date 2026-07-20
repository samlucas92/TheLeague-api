using TheLeague.Enums;

namespace TheLeague.Models;

public class Challenge
{
	public Guid Id { get; set; }
	public Guid LeagueId { get; set; }
	public Guid CreatedByUserId { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public List<Guid> TargetMemberIds { get; set; } = [];
	public List<Guid> AcceptedMemberIds { get; set; } = [];
	public List<Guid> RejectedMemberIds { get; set; } = [];
	public List<Guid> CompletedMemberIds { get; set; } = [];
	public List<Guid> FailedMemberIds { get; set; } = [];
	public int PointsForSuccess { get; set; }
	public int PointsForFailure { get; set; }
	public ChallengeScoringType ScoringType { get; set; }
	public int? FixedPoints { get; set; }
	public int? MinimumPoints { get; set; }
	public int? MaximumPoints { get; set; }
	public ChallengeRepeatType RepeatType { get; set; }
	public int? SubmissionLimit { get; set; }
	public bool RequiresEvidence { get; set; }
	public bool IsSecret { get; set; }
	public bool IsActive { get; set; }
	public DateTime? AvailableFrom { get; set; }
	public DateTime? AvailableUntil { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime? UpdatedAt { get; set; }
}
