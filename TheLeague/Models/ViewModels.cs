using TheLeague.Enums;

namespace TheLeague.Models;

public record AuthenticatedUser(Guid Id, string Name, string EmailAddress);

public record LeagueSummary(
	Guid Id,
	string Name,
	string? Description,
	LeagueStatus Status,
	LeagueMemberRole Role,
	LeagueMemberStatus MembershipStatus,
	string JoinCode);

public record JoinPreview(Guid LeagueId, string Name, string? Description, string OwnerName, LeagueJoinMode JoinMode, bool RequiresApproval);

public record LeaderboardRow(int Position, Guid LeagueMemberId, string DisplayName, int ApprovedPoints, int PendingPoints);

public record PointsFeedItem(
	Guid AllocationId,
	Guid LeagueMemberId,
	string DisplayName,
	int Points,
	string Reason,
	PointAllocationSource Source,
	string? ChallengeName,
	string AwardedByName,
	DateTime AwardedAt);

public record ManualPointsResult(string Status, PointAllocation? Allocation, PointSubmission? Submission);

public record SubmissionListItem(
	Guid Id,
	Guid? ChallengeId,
	string ChallengeName,
	Guid LeagueMemberId,
	string DisplayName,
	int? RequestedPoints,
	int? ApprovedPoints,
	string PublicReason,
	PointSubmissionStatus Status,
	DateTime SubmittedAt,
	DateTime? ReviewedAt);

public record PublicLeagueView(
	League League,
	IReadOnlyCollection<LeagueMember> Members,
	IReadOnlyCollection<LeaderboardRow> Leaderboard,
	IReadOnlyCollection<PointsFeedItem> PointsFeed,
	IReadOnlyCollection<ChallengeListItem> Challenges);

public record ChallengeListItem(
	Guid Id,
	Guid CreatedByUserId,
	string Name,
	string? Description,
	IReadOnlyCollection<Guid> TargetMemberIds,
	IReadOnlyCollection<string> TargetNames,
	IReadOnlyCollection<Guid> AcceptedMemberIds,
	IReadOnlyCollection<Guid> RejectedMemberIds,
	IReadOnlyCollection<Guid> CompletedMemberIds,
	IReadOnlyCollection<Guid> FailedMemberIds,
	int PointsForSuccess,
	int PointsForFailure,
	bool IsActive,
	DateTime CreatedAt,
	IReadOnlyCollection<ChallengeOutcomeItem> Outcomes);

public record ChallengeOutcomeItem(
	Guid LeagueMemberId,
	string DisplayName,
	string Status,
	DateTime? AwardedAt,
	string? AwardedByName,
	int? Points);
