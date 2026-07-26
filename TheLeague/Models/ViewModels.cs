using TheLeague.Enums;

namespace TheLeague.Models;

public record AuthenticatedUser(Guid Id, string Name, string EmailAddress, bool IsEmailVerified, bool IsSiteAdmin);

public record ForgotPasswordResult(bool TokenCreated, string? ResetToken, DateTime? ExpiresAt);

public record EmailVerificationResult(bool TokenCreated, string? VerificationToken, DateTime? ExpiresAt);

public record EmailSendResult(bool Succeeded, string? ProviderMessageId, string? FailureReason);

public record EmailAuditItem(
	Guid Id,
	string Provider,
	string Status,
	string ToEmailAddress,
	string? ToName,
	string Subject,
	int Attempts,
	string? ProviderMessageId,
	string? FailureReason,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	DateTime? SentAt,
	DateTime? NextAttemptAt);

public record SiteUserAdminItem(
	Guid Id,
	string Name,
	string EmailAddress,
	bool IsEmailVerified,
	bool IsSiteAdmin,
	bool IsDeleted,
	DateTime CreatedAt,
	DateTime? EmailVerifiedAt,
	DateTime? DeletedAt);

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

public record LeagueAuditItem(
	Guid Id,
	LeagueAuditAction Action,
	Guid PerformedByUserId,
	string PerformedByName,
	string EntityType,
	Guid? EntityId,
	string Summary,
	DateTime CreatedAt);
