using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class PointSubmissionService(LeagueDataContext context, ILeagueAuthorisationService authorisationService) : IPointSubmissionService
{
	public async Task<PointSubmission> CreateAsync(Guid leagueId, Guid userId, Guid challengeId, Guid? leagueMemberId, int? requestedPoints, string publicReason, CancellationToken cancellationToken = default)
	{
		var requesterMembership = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var challenge = GetChallenge(leagueId, challengeId);
		var targetMember = leagueMemberId.HasValue ? GetMember(leagueId, leagueMemberId.Value) : requesterMembership;

		if (targetMember.Id != requesterMembership.Id && requesterMembership.Role == LeagueMemberRole.Participant)
		{
			throw new UnauthorizedAccessException("Participants cannot submit for another member.");
		}

		if (challenge.RepeatType == ChallengeRepeatType.AdminOnly && requesterMembership.Role == LeagueMemberRole.Participant)
		{
			throw new InvalidOperationException("This challenge can only be submitted by admins.");
		}

		if (!challenge.IsActive)
		{
			throw new InvalidOperationException("This challenge is not active.");
		}

		if (challenge.AvailableFrom.HasValue && challenge.AvailableFrom.Value > DateTime.UtcNow)
		{
			throw new InvalidOperationException("This challenge is not available yet.");
		}

		if (challenge.AvailableUntil.HasValue && challenge.AvailableUntil.Value < DateTime.UtcNow)
		{
			throw new InvalidOperationException("This challenge is no longer available.");
		}

		var points = challenge.ScoringType == ChallengeScoringType.Fixed ? challenge.FixedPoints : requestedPoints;
		if (challenge.ScoringType == ChallengeScoringType.ParticipantEntered)
		{
			if (!points.HasValue)
			{
				throw new InvalidOperationException("Requested points are required.");
			}

			if (challenge.MinimumPoints.HasValue && points.Value < challenge.MinimumPoints.Value)
			{
				throw new InvalidOperationException("Requested points are below the minimum.");
			}

			if (challenge.MaximumPoints.HasValue && points.Value > challenge.MaximumPoints.Value)
			{
				throw new InvalidOperationException("Requested points are above the maximum.");
			}
		}

		if (string.IsNullOrWhiteSpace(publicReason))
		{
			throw new InvalidOperationException("A public reason is required.");
		}

		var submission = new PointSubmission
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			ChallengeId = challengeId,
			LeagueMemberId = targetMember.Id,
			SubmittedByUserId = userId,
			RequestedPoints = points,
			PublicReason = publicReason.Trim(),
			Status = PointSubmissionStatus.Pending,
			SubmittedAt = DateTime.UtcNow
		};

		context.Submissions[submission.Id] = submission;
		return submission;
	}

	public async Task<IReadOnlyCollection<SubmissionListItem>> ListMineAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		var membership = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);

		return context.Submissions.Values
			.Where(submission => submission.LeagueId == leagueId && submission.LeagueMemberId == membership.Id)
			.OrderByDescending(submission => submission.SubmittedAt)
			.Select(ToListItem)
			.ToArray();
	}

	public async Task<IReadOnlyCollection<SubmissionListItem>> ListPendingAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredPointApproverMembershipAsync(leagueId, userId, cancellationToken);

		return context.Submissions.Values
			.Where(submission => submission.LeagueId == leagueId && submission.Status == PointSubmissionStatus.Pending)
			.OrderBy(submission => submission.SubmittedAt)
			.Select(ToListItem)
			.ToArray();
	}

	public async Task<(PointSubmission Submission, PointAllocation Allocation)> ApproveAsync(Guid leagueId, Guid userId, Guid submissionId, int? approvedPoints, string publicReviewReason, string? adminReviewNote, CancellationToken cancellationToken = default)
	{
		var reviewer = await authorisationService.GetRequiredPointApproverMembershipAsync(leagueId, userId, cancellationToken);
		var submission = GetSubmission(leagueId, submissionId);

		if (submission.Status != PointSubmissionStatus.Pending)
		{
			throw new InvalidOperationException("Only pending submissions can be approved.");
		}

		if (submission.SubmittedByUserId == userId && reviewer.Id == submission.LeagueMemberId)
		{
			throw new InvalidOperationException("Participants cannot approve their own submission.");
		}

		if (context.Allocations.Values.Any(allocation => allocation.SubmissionId == submission.Id && allocation.Source == PointAllocationSource.ApprovedSubmission))
		{
			throw new InvalidOperationException("This submission already has an approved allocation.");
		}

		var challenge = GetChallenge(leagueId, submission.ChallengeId);
		var points = approvedPoints ?? submission.RequestedPoints ?? challenge.FixedPoints;
		if (!points.HasValue)
		{
			throw new InvalidOperationException("Approved points are required.");
		}

		submission.Status = PointSubmissionStatus.Approved;
		submission.ReviewedByUserId = userId;
		submission.ApprovedPoints = points;
		submission.PublicReviewReason = string.IsNullOrWhiteSpace(publicReviewReason) ? submission.PublicReason : publicReviewReason.Trim();
		submission.AdminReviewNote = string.IsNullOrWhiteSpace(adminReviewNote) ? null : adminReviewNote.Trim();
		submission.ReviewedAt = DateTime.UtcNow;
		await context.Submissions.SaveAsync(submission, cancellationToken);

		var allocation = new PointAllocation
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			LeagueMemberId = submission.LeagueMemberId,
			ChallengeId = submission.ChallengeId,
			SubmissionId = submission.Id,
			Points = points.Value,
			Reason = submission.PublicReviewReason,
			Source = PointAllocationSource.ApprovedSubmission,
			AwardedByUserId = userId,
			AwardedAt = DateTime.UtcNow
		};

		context.Allocations[allocation.Id] = allocation;
		return (submission, allocation);
	}

	public async Task<PointSubmission> RejectAsync(Guid leagueId, Guid userId, Guid submissionId, string publicReviewReason, string? adminReviewNote, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredPointApproverMembershipAsync(leagueId, userId, cancellationToken);
		var submission = GetSubmission(leagueId, submissionId);

		if (submission.Status != PointSubmissionStatus.Pending)
		{
			throw new InvalidOperationException("Only pending submissions can be rejected.");
		}

		if (string.IsNullOrWhiteSpace(publicReviewReason))
		{
			throw new InvalidOperationException("A public rejection reason is required.");
		}

		submission.Status = PointSubmissionStatus.Rejected;
		submission.ReviewedByUserId = userId;
		submission.PublicReviewReason = publicReviewReason.Trim();
		submission.AdminReviewNote = string.IsNullOrWhiteSpace(adminReviewNote) ? null : adminReviewNote.Trim();
		submission.ReviewedAt = DateTime.UtcNow;
		await context.Submissions.SaveAsync(submission, cancellationToken);

		return submission;
	}

	private Challenge GetChallenge(Guid leagueId, Guid challengeId)
	{
		if (!context.Challenges.TryGetValue(challengeId, out var challenge) || challenge.LeagueId != leagueId)
		{
			throw new InvalidOperationException("Challenge was not found.");
		}

		return challenge;
	}

	private LeagueMember GetMember(Guid leagueId, Guid leagueMemberId)
	{
		if (!context.Members.TryGetValue(leagueMemberId, out var member) || member.LeagueId != leagueId || member.Status != LeagueMemberStatus.Active)
		{
			throw new InvalidOperationException("League member was not found.");
		}

		return member;
	}

	private PointSubmission GetSubmission(Guid leagueId, Guid submissionId)
	{
		if (!context.Submissions.TryGetValue(submissionId, out var submission) || submission.LeagueId != leagueId)
		{
			throw new InvalidOperationException("Submission was not found.");
		}

		return submission;
	}

	private SubmissionListItem ToListItem(PointSubmission submission)
	{
		var challenge = context.Challenges[submission.ChallengeId];
		var member = context.Members[submission.LeagueMemberId];
		return new SubmissionListItem(
			submission.Id,
			submission.ChallengeId,
			challenge.Name,
			submission.LeagueMemberId,
			member.DisplayName,
			submission.RequestedPoints,
			submission.ApprovedPoints,
			submission.PublicReason,
			submission.Status,
			submission.SubmittedAt,
			submission.ReviewedAt);
	}
}
