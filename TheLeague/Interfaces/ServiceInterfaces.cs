using TheLeague.Enums;
using TheLeague.Models;

namespace TheLeague.Interfaces;

public interface IUserService
{
	Task<UserAccount> RegisterAsync(string name, string emailAddress, string password, CancellationToken cancellationToken = default);
	Task<UserAccount?> ValidateCredentialsAsync(string emailAddress, string password, CancellationToken cancellationToken = default);
	Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<UserAccount?> GetByEmailAsync(string emailAddress, CancellationToken cancellationToken = default);
	Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
	Task<ForgotPasswordResult> RequestPasswordResetAsync(string emailAddress, CancellationToken cancellationToken = default);
	Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
	Task<EmailVerificationResult> RequestEmailVerificationAsync(Guid userId, CancellationToken cancellationToken = default);
	Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default);
	Task<UserAccount> EnsureSiteAdminStatusAsync(UserAccount account, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<SiteUserAdminItem>> ListForSiteAdminAsync(CancellationToken cancellationToken = default);
	Task<UserAccount> SetSiteAdminAsync(Guid actingUserId, Guid targetUserId, bool isSiteAdmin, CancellationToken cancellationToken = default);
	Task DeactivateAsync(Guid userId, string currentPassword, CancellationToken cancellationToken = default);
}

public interface ILeagueAuthorisationService
{
	Task<LeagueMember> GetRequiredMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<LeagueMember> GetRequiredAdminMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<LeagueMember> GetRequiredOwnerMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<LeagueMember> GetRequiredPointApproverMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
}

public interface ILeagueService
{
	Task<League> CreateAsync(Guid ownerUserId, string name, string? description, LeaguePresetType presetType, LeagueJoinMode joinMode, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<LeagueSummary>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<League?> GetAsync(Guid leagueId, CancellationToken cancellationToken = default);
	Task<League> UpdateSettingsAsync(Guid leagueId, Guid userId, string name, string? description, LeagueJoinMode joinMode, bool publicViewEnabled, CancellationToken cancellationToken = default);
	Task<League> RegenerateJoinCodeAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<JoinPreview> PreviewJoinAsync(string joinCode, CancellationToken cancellationToken = default);
	Task<PublicLeagueView> GetPublicViewAsync(string joinCode, CancellationToken cancellationToken = default);
}

public interface ILeagueMemberService
{
	Task<IReadOnlyCollection<LeagueMember>> ListAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<LeagueMember> JoinAsync(Guid userId, string joinCode, string displayName, CancellationToken cancellationToken = default);
	Task<LeagueMember> CreateOwnerAsync(Guid leagueId, Guid ownerUserId, string displayName, CancellationToken cancellationToken = default);
	Task<LeagueMember> AddOfflineMemberAsync(Guid leagueId, Guid userId, string displayName, string? emailAddress, LeagueMemberRole role, CancellationToken cancellationToken = default);
	Task<LeagueMember> UpdateAsync(Guid leagueId, Guid userId, Guid memberId, string displayName, string? emailAddress, LeagueMemberRole role, CancellationToken cancellationToken = default);
	Task<LeagueMember> ChangeRoleAsync(Guid leagueId, Guid userId, Guid memberId, LeagueMemberRole role, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid leagueId, Guid userId, Guid memberId, CancellationToken cancellationToken = default);
	Task<LeagueMember> LinkOfflineMemberAsync(Guid leagueId, Guid userId, Guid memberId, string emailAddress, CancellationToken cancellationToken = default);
}

public interface ILeaguePresetService
{
	Task ApplyPresetAsync(Guid leagueId, LeaguePresetType presetType, CancellationToken cancellationToken = default);
}

public interface IChallengeService
{
	Task<IReadOnlyCollection<ChallengeListItem>> ListAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<ChallengeListItem>> ListPublicAsync(Guid leagueId, CancellationToken cancellationToken = default);
	Task<Challenge> CreateAsync(Guid leagueId, Guid userId, Challenge challenge, CancellationToken cancellationToken = default);
	Task<Challenge> UpdateAsync(Guid leagueId, Guid userId, Guid challengeId, Challenge challenge, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid leagueId, Guid userId, Guid challengeId, CancellationToken cancellationToken = default);
	Task<Challenge> AcceptAsync(Guid leagueId, Guid userId, Guid challengeId, CancellationToken cancellationToken = default);
	Task<(Challenge Challenge, PointAllocation Penalty)> RejectAsync(Guid leagueId, Guid userId, Guid challengeId, CancellationToken cancellationToken = default);
	Task<(Challenge Challenge, PointAllocation Allocation)> CompleteAsync(Guid leagueId, Guid userId, Guid challengeId, Guid? targetMemberId = null, CancellationToken cancellationToken = default);
	Task<(Challenge Challenge, PointAllocation Allocation)> FailAsync(Guid leagueId, Guid userId, Guid challengeId, Guid? targetMemberId = null, CancellationToken cancellationToken = default);
}

public interface ITournamentService
{
	Task<IReadOnlyCollection<TournamentListItem>> ListAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<TournamentListItem>> ListPublicAsync(Guid leagueId, CancellationToken cancellationToken = default);
	Task<TournamentListItem> CreateAsync(Guid leagueId, Guid userId, Tournament tournament, IReadOnlyCollection<Guid> participantMemberIds, CancellationToken cancellationToken = default);
	Task<TournamentListItem> CompleteMatchAsync(Guid leagueId, Guid userId, Guid tournamentId, Guid matchId, Guid winnerMemberId, int? playerOneScore, int? playerTwoScore, CancellationToken cancellationToken = default);
	Task<TournamentListItem> ScoreRoundAsync(Guid leagueId, Guid userId, Guid tournamentId, IReadOnlyDictionary<Guid, int> scores, CancellationToken cancellationToken = default);
	Task<TournamentListItem> ScorePubGolfHoleAsync(Guid leagueId, Guid userId, Guid tournamentId, Guid holeId, IReadOnlyDictionary<Guid, int?> scores, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid leagueId, Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);
}

public interface IPointSubmissionService
{
	Task<PointSubmission> CreateAsync(Guid leagueId, Guid userId, Guid challengeId, Guid? leagueMemberId, int? requestedPoints, string publicReason, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<SubmissionListItem>> ListMineAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<SubmissionListItem>> ListPendingAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<(PointSubmission Submission, PointAllocation Allocation)> ApproveAsync(Guid leagueId, Guid userId, Guid submissionId, int? approvedPoints, string publicReviewReason, string? adminReviewNote, CancellationToken cancellationToken = default);
	Task<PointSubmission> RejectAsync(Guid leagueId, Guid userId, Guid submissionId, string publicReviewReason, string? adminReviewNote, CancellationToken cancellationToken = default);
}

public interface IPointAllocationService
{
	Task<ManualPointsResult> CreateManualAsync(Guid leagueId, Guid userId, Guid leagueMemberId, int points, string reason, CancellationToken cancellationToken = default);
	Task<PointAllocation> UpdateAsync(Guid leagueId, Guid userId, Guid allocationId, Guid leagueMemberId, int points, string reason, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid leagueId, Guid userId, Guid allocationId, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<PointsFeedItem>> ListFeedAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<PointsFeedItem>> ListPublicFeedAsync(Guid leagueId, CancellationToken cancellationToken = default);
}

public interface ILeaderboardService
{
	Task<IReadOnlyCollection<LeaderboardRow>> GetAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<LeaderboardRow>> GetPublicAsync(Guid leagueId, CancellationToken cancellationToken = default);
}

public interface ILeagueAuditService
{
	Task RecordAsync(Guid leagueId, Guid performedByUserId, LeagueAuditAction action, string entityType, Guid? entityId, string summary, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<LeagueAuditItem>> ListAsync(Guid leagueId, Guid userId, int take = 50, CancellationToken cancellationToken = default);
}

public interface IEmailOutboxService
{
	Task<EmailMessage> QueueAsync(string toEmailAddress, string? toName, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default);
	Task<EmailMessage> SendAsync(Guid emailMessageId, CancellationToken cancellationToken = default);
	Task<IReadOnlyCollection<EmailAuditItem>> ListRecentAsync(int take = 50, CancellationToken cancellationToken = default);
}

public interface IEmailSender
{
	Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
