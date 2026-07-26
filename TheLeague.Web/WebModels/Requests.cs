using TheLeague.Enums;

namespace TheLeague.Web.WebModels;

public record RegisterRequest(string Name, string EmailAddress, string Password, bool AcceptedTerms);
public record LoginRequest(string EmailAddress, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ForgotPasswordRequest(string EmailAddress);
public record ResetPasswordRequest(string Token, string NewPassword);
public record VerifyEmailRequest(string Token);
public record UpdateSiteAdminRequest(bool IsSiteAdmin);
public record CreateLeagueRequest(string Name, string? Description, LeaguePresetType PresetType, LeagueJoinMode JoinMode);
public record UpdateLeagueSettingsRequest(string Name, string? Description, LeagueJoinMode JoinMode, bool PublicViewEnabled);
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
public record UpdateChallengeRequest(
	string Name,
	string? Description,
	IReadOnlyCollection<Guid>? TargetMemberIds,
	int PointsForSuccess,
	int PointsForFailure,
	bool IsActive);
public record ChallengeOutcomeRequest(Guid? TargetMemberId);
public record CreateSubmissionRequest(Guid ChallengeId, Guid? LeagueMemberId, int? RequestedPoints, string PublicReason);
public record ApproveSubmissionRequest(int? ApprovedPoints, string PublicReviewReason, string? AdminReviewNote);
public record RejectSubmissionRequest(string PublicReviewReason, string? AdminReviewNote);
public record CreateManualAllocationRequest(Guid LeagueMemberId, int Points, string Reason);
public record UpdateManualAllocationRequest(Guid LeagueMemberId, int Points, string Reason);
public record AddOfflineMemberRequest(string DisplayName, string? EmailAddress, LeagueMemberRole Role);
public record UpdateMemberRequest(string DisplayName, string? EmailAddress, LeagueMemberRole Role);
public record ChangeMemberRoleRequest(LeagueMemberRole Role);
public record LinkOfflineMemberRequest(string EmailAddress);
