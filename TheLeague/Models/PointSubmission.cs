using TheLeague.Enums;

namespace TheLeague.Models;

public class PointSubmission
{
	public Guid Id { get; set; }
	public Guid LeagueId { get; set; }
	public Guid ChallengeId { get; set; }
	public Guid LeagueMemberId { get; set; }
	public Guid SubmittedByUserId { get; set; }
	public int? RequestedPoints { get; set; }
	public string PublicReason { get; set; } = string.Empty;
	public string? EvidenceFileKey { get; set; }
	public PointSubmissionStatus Status { get; set; }
	public Guid? ReviewedByUserId { get; set; }
	public int? ApprovedPoints { get; set; }
	public string? PublicReviewReason { get; set; }
	public string? AdminReviewNote { get; set; }
	public DateTime SubmittedAt { get; set; }
	public DateTime? ReviewedAt { get; set; }
	public DateTime? CancelledAt { get; set; }
}
