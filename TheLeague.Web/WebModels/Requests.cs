using TheLeague.Enums;

namespace TheLeague.Web.WebModels;

public record RegisterRequest(string Name, string EmailAddress, string Password);
public record LoginRequest(string EmailAddress, string Password);
public record CreateLeagueRequest(string Name, string? Description, LeaguePresetType PresetType, LeagueJoinMode JoinMode);
public record JoinPreviewRequest(string JoinCode);
public record JoinLeagueRequest(string JoinCode, string DisplayName);
public record CreateChallengeRequest(
	string Name,
	string? Description,
	IReadOnlyCollection<Guid>? TargetMemberIds,
	int? PointsForSuccess,
	int? PointsForFailure,
	ChallengeScoringType? ScoringType,
	int? FixedPoints,
	int? MinimumPoints,
	int? MaximumPoints,
	ChallengeRepeatType RepeatType,
	int? SubmissionLimit,
	bool RequiresEvidence,
	bool IsSecret);
public record CreateSubmissionRequest(Guid ChallengeId, Guid? LeagueMemberId, int? RequestedPoints, string PublicReason);
public record ApproveSubmissionRequest(int? ApprovedPoints, string PublicReviewReason, string? AdminReviewNote);
public record RejectSubmissionRequest(string PublicReviewReason, string? AdminReviewNote);
public record CreateManualAllocationRequest(Guid LeagueMemberId, int Points, string Reason);
public record AddOfflineMemberRequest(string DisplayName, string? EmailAddress, LeagueMemberRole Role);
public record ChangeMemberRoleRequest(LeagueMemberRole Role);
public record LinkOfflineMemberRequest(string EmailAddress);
