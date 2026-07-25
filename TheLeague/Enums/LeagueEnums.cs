namespace TheLeague.Enums;

public enum LeagueStatus
{
	Draft,
	Active,
	Closed,
	Archived
}

public enum LeagueJoinMode
{
	OpenWithCode,
	ApprovalRequired,
	InviteOnly,
	Closed
}

public enum LeaguePresetType
{
	Custom,
	Stag,
	Fitness,
	Workplace,
	Fundraising,
	Gaming
}

public enum LeagueMemberRole
{
	Participant,
	PointApprover,
	Admin,
	Owner
}

public enum LeagueMemberStatus
{
	Pending,
	Active,
	Removed,
	Left
}

public enum ChallengeScoringType
{
	Fixed,
	ParticipantEntered,
	AdminDecided
}

public enum ChallengeRepeatType
{
	OncePerLeague,
	OncePerDay,
	Limited,
	Unlimited,
	AdminOnly
}

public enum PointSubmissionStatus
{
	Pending,
	Approved,
	Rejected,
	Cancelled
}

public enum PointAllocationSource
{
	ApprovedSubmission,
	AdminAward,
	AdminPenalty,
	Adjustment,
	Reversal,
	PresetBonus
}

public enum LeagueAuditAction
{
	LeagueCreated,
	LeagueSettingsChanged,
	JoinCodeRegenerated,
	MemberInvited,
	MemberJoined,
	MemberApproved,
	MemberRemoved,
	RoleChanged,
	OfflineMemberAdded,
	MemberUpdated,
	OfflineMemberLinked,
	ChallengeCreated,
	ChallengeEdited,
	ChallengeDeleted,
	ChallengeDisabled,
	ChallengeAccepted,
	ChallengeRejected,
	ChallengeCompleted,
	ChallengeFailed,
	SubmissionCreated,
	SubmissionApproved,
	SubmissionRejected,
	PointsAwarded,
	PointsDeducted,
	PointsRequested,
	AllocationEdited,
	AllocationDeleted,
	AllocationReversed,
	LeagueClosed,
	LeagueArchived
}
