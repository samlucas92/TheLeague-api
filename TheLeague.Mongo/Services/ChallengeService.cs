using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class ChallengeService(LeagueDataContext context, ILeagueAuthorisationService authorisationService, ILeagueAuditService auditService) : IChallengeService
{
	public async Task<IReadOnlyCollection<ChallengeListItem>> ListAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		return await ListPublicAsync(leagueId, cancellationToken);
	}

	public Task<IReadOnlyCollection<ChallengeListItem>> ListPublicAsync(Guid leagueId, CancellationToken cancellationToken = default)
	{
		var challenges = context.Challenges.Values
			.Where(challenge => challenge.LeagueId == leagueId)
			.OrderByDescending(challenge => challenge.IsActive)
			.ThenByDescending(challenge => challenge.CreatedAt)
			.Select(ToListItem)
			.ToArray();

		return Task.FromResult<IReadOnlyCollection<ChallengeListItem>>(challenges);
	}

	public async Task<Challenge> CreateAsync(Guid leagueId, Guid userId, Challenge challenge, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);

		var distinctTargets = ValidateChallenge(leagueId, challenge);

		challenge.Id = Guid.NewGuid();
		challenge.LeagueId = leagueId;
		challenge.CreatedByUserId = userId;
		challenge.Name = challenge.Name.Trim();
		challenge.Description = string.IsNullOrWhiteSpace(challenge.Description) ? null : challenge.Description.Trim();
		challenge.TargetMemberIds = distinctTargets;
		challenge.AcceptedMemberIds = [];
		challenge.RejectedMemberIds = [];
		challenge.CompletedMemberIds = [];
		challenge.FailedMemberIds = [];
		challenge.PointsForSuccess = challenge.PointsForSuccess;
		challenge.PointsForFailure = challenge.PointsForFailure > 0 ? -challenge.PointsForFailure : challenge.PointsForFailure;
		challenge.ScoringType = ChallengeScoringType.Fixed;
		challenge.FixedPoints = challenge.PointsForSuccess;
		challenge.RepeatType = ChallengeRepeatType.Unlimited;
		challenge.IsActive = true;
		challenge.CreatedAt = DateTime.UtcNow;

		context.Challenges[challenge.Id] = challenge;
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.ChallengeCreated, "Challenge", challenge.Id, $"Created challenge {challenge.Name}.", cancellationToken);
		return challenge;
	}

	public async Task<Challenge> UpdateAsync(Guid leagueId, Guid userId, Guid challengeId, Challenge updates, CancellationToken cancellationToken = default)
	{
		var membership = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var challenge = GetChallenge(leagueId, challengeId);
		EnsureCanMaintainChallenge(membership, challenge);
		var distinctTargets = ValidateChallenge(leagueId, updates);

		challenge.Name = updates.Name.Trim();
		challenge.Description = string.IsNullOrWhiteSpace(updates.Description) ? null : updates.Description.Trim();
		challenge.TargetMemberIds = distinctTargets;
		challenge.AcceptedMemberIds = challenge.AcceptedMemberIds.Where(distinctTargets.Contains).Distinct().ToList();
		challenge.RejectedMemberIds = challenge.RejectedMemberIds.Where(distinctTargets.Contains).Distinct().ToList();
		challenge.CompletedMemberIds = challenge.CompletedMemberIds.Where(distinctTargets.Contains).Distinct().ToList();
		challenge.FailedMemberIds = challenge.FailedMemberIds.Where(distinctTargets.Contains).Distinct().ToList();
		challenge.PointsForSuccess = updates.PointsForSuccess;
		challenge.PointsForFailure = updates.PointsForFailure > 0 ? -updates.PointsForFailure : updates.PointsForFailure;
		challenge.FixedPoints = challenge.PointsForSuccess;
		challenge.IsActive = updates.IsActive;
		challenge.UpdatedAt = DateTime.UtcNow;
		await context.Challenges.SaveAsync(challenge, cancellationToken);
		await auditService.RecordAsync(leagueId, userId, updates.IsActive ? LeagueAuditAction.ChallengeEdited : LeagueAuditAction.ChallengeDisabled, "Challenge", challenge.Id, $"Updated challenge {challenge.Name}.", cancellationToken);
		return challenge;
	}

	public async Task DeleteAsync(Guid leagueId, Guid userId, Guid challengeId, CancellationToken cancellationToken = default)
	{
		var membership = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var challenge = GetChallenge(leagueId, challengeId);
		EnsureCanMaintainChallenge(membership, challenge);
		await context.Challenges.RemoveAsync(challengeId, cancellationToken);
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.ChallengeDeleted, "Challenge", challengeId, $"Deleted challenge {challenge.Name}.", cancellationToken);
	}

	public async Task<Challenge> AcceptAsync(Guid leagueId, Guid userId, Guid challengeId, CancellationToken cancellationToken = default)
	{
		var member = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var challenge = GetTargetedChallenge(leagueId, challengeId, member.Id);

		if (!challenge.AcceptedMemberIds.Contains(member.Id))
		{
			challenge.AcceptedMemberIds.Add(member.Id);
		}

		challenge.RejectedMemberIds.Remove(member.Id);
		challenge.UpdatedAt = DateTime.UtcNow;
		await context.Challenges.SaveAsync(challenge, cancellationToken);
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.ChallengeAccepted, "Challenge", challenge.Id, $"{member.DisplayName} accepted challenge {challenge.Name}.", cancellationToken);
		return challenge;
	}

	public async Task<(Challenge Challenge, PointAllocation Penalty)> RejectAsync(Guid leagueId, Guid userId, Guid challengeId, CancellationToken cancellationToken = default)
	{
		var member = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var challenge = GetTargetedChallenge(leagueId, challengeId, member.Id);

		if (!challenge.RejectedMemberIds.Contains(member.Id))
		{
			challenge.RejectedMemberIds.Add(member.Id);
		}

		challenge.AcceptedMemberIds.Remove(member.Id);
		challenge.UpdatedAt = DateTime.UtcNow;
		await context.Challenges.SaveAsync(challenge, cancellationToken);

		var allocation = GetOrCreatePenaltyAllocation(leagueId, userId, member.Id, challenge, "Rejected challenge");
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.ChallengeRejected, "Challenge", challenge.Id, $"{member.DisplayName} rejected challenge {challenge.Name}.", cancellationToken);
		return (challenge, allocation);
	}

	public async Task<(Challenge Challenge, PointAllocation Allocation)> CompleteAsync(Guid leagueId, Guid userId, Guid challengeId, Guid? targetMemberId = null, CancellationToken cancellationToken = default)
	{
		var actor = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var challenge = GetChallenge(leagueId, challengeId);
		var memberId = ResolveOutcomeTarget(actor, challenge, targetMemberId);
		EnsureCanConfirmOutcome(actor, challenge, memberId);

		if (challenge.RejectedMemberIds.Contains(memberId))
		{
			throw new InvalidOperationException("Rejected challenges cannot be completed.");
		}

		if (challenge.FailedMemberIds.Contains(memberId))
		{
			throw new InvalidOperationException("Failed challenges cannot be marked as completed.");
		}

		if (!challenge.AcceptedMemberIds.Contains(memberId))
		{
			throw new InvalidOperationException("Only accepted challenges can be completed.");
		}

		if (!challenge.CompletedMemberIds.Contains(memberId))
		{
			challenge.CompletedMemberIds.Add(memberId);
		}

		challenge.UpdatedAt = DateTime.UtcNow;
		await context.Challenges.SaveAsync(challenge, cancellationToken);

		var existingAllocation = context.Allocations.Values
			.Where(allocation =>
				allocation.ChallengeId == challenge.Id &&
				allocation.LeagueMemberId == memberId &&
				allocation.Source == PointAllocationSource.AdminAward)
			.OrderByDescending(allocation => allocation.AwardedAt)
			.FirstOrDefault();
		if (existingAllocation is not null)
		{
			return (challenge, existingAllocation);
		}

		var allocation = new PointAllocation
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			LeagueMemberId = memberId,
			ChallengeId = challenge.Id,
			Points = challenge.PointsForSuccess,
			Reason = $"Completed challenge: {challenge.Name}",
			Source = PointAllocationSource.AdminAward,
			AwardedByUserId = userId,
			AwardedAt = DateTime.UtcNow
		};

		context.Allocations[allocation.Id] = allocation;
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.ChallengeCompleted, "Challenge", challenge.Id, $"Marked challenge {challenge.Name} completed.", cancellationToken);
		return (challenge, allocation);
	}

	public async Task<(Challenge Challenge, PointAllocation Allocation)> FailAsync(Guid leagueId, Guid userId, Guid challengeId, Guid? targetMemberId = null, CancellationToken cancellationToken = default)
	{
		var actor = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var challenge = GetChallenge(leagueId, challengeId);
		var memberId = ResolveOutcomeTarget(actor, challenge, targetMemberId);
		EnsureCanConfirmOutcome(actor, challenge, memberId);

		if (challenge.CompletedMemberIds.Contains(memberId))
		{
			throw new InvalidOperationException("Completed challenges cannot be marked as failed.");
		}

		if (challenge.RejectedMemberIds.Contains(memberId))
		{
			throw new InvalidOperationException("Rejected challenges cannot be marked as failed.");
		}

		if (!challenge.AcceptedMemberIds.Contains(memberId))
		{
			throw new InvalidOperationException("Only accepted challenges can be failed.");
		}

		if (!challenge.FailedMemberIds.Contains(memberId))
		{
			challenge.FailedMemberIds.Add(memberId);
		}

		challenge.UpdatedAt = DateTime.UtcNow;
		await context.Challenges.SaveAsync(challenge, cancellationToken);

		var allocation = GetOrCreatePenaltyAllocation(leagueId, userId, memberId, challenge, "Failed challenge");
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.ChallengeFailed, "Challenge", challenge.Id, $"Marked challenge {challenge.Name} failed.", cancellationToken);
		return (challenge, allocation);
	}

	private Challenge GetTargetedChallenge(Guid leagueId, Guid challengeId, Guid memberId)
	{
		var challenge = GetChallenge(leagueId, challengeId);

		if (!challenge.TargetMemberIds.Contains(memberId))
		{
			throw new UnauthorizedAccessException("This challenge is not aimed at you.");
		}

		return challenge;
	}

	private Challenge GetChallenge(Guid leagueId, Guid challengeId)
	{
		if (!context.Challenges.TryGetValue(challengeId, out var challenge) || challenge.LeagueId != leagueId)
		{
			throw new InvalidOperationException("Challenge was not found.");
		}

		return challenge;
	}

	private List<Guid> ValidateChallenge(Guid leagueId, Challenge challenge)
	{
		if (string.IsNullOrWhiteSpace(challenge.Name))
		{
			throw new InvalidOperationException("Challenge name is required.");
		}

		if (challenge.TargetMemberIds.Count == 0)
		{
			throw new InvalidOperationException("Choose at least one challenged person.");
		}

		var distinctTargets = challenge.TargetMemberIds.Distinct().ToList();
		foreach (var targetMemberId in distinctTargets)
		{
			if (!context.Members.TryGetValue(targetMemberId, out var member) || member.LeagueId != leagueId || member.Status != LeagueMemberStatus.Active)
			{
				throw new InvalidOperationException("A challenged person was not found.");
			}
		}

		if (challenge.PointsForSuccess == 0)
		{
			throw new InvalidOperationException("Completion points are required.");
		}

		return distinctTargets;
	}

	private static void EnsureCanMaintainChallenge(LeagueMember membership, Challenge challenge)
	{
		if (challenge.CreatedByUserId == membership.UserId || membership.Role is LeagueMemberRole.Admin or LeagueMemberRole.Owner)
		{
			return;
		}

		throw new UnauthorizedAccessException("Only the challenge creator or a league admin can change this challenge.");
	}

	private static Guid ResolveOutcomeTarget(LeagueMember actor, Challenge challenge, Guid? targetMemberId)
	{
		var memberId = targetMemberId ?? actor.Id;
		if (!challenge.TargetMemberIds.Contains(memberId))
		{
			throw new UnauthorizedAccessException("This challenge is not aimed at that member.");
		}

		return memberId;
	}

	private static void EnsureCanConfirmOutcome(LeagueMember actor, Challenge challenge, Guid targetMemberId)
	{
		if (challenge.CreatedByUserId == actor.UserId || actor.Role is LeagueMemberRole.Admin or LeagueMemberRole.Owner)
		{
			return;
		}

		if (actor.Id == targetMemberId)
		{
			throw new UnauthorizedAccessException("The challenge creator or a league admin must confirm completion or failure.");
		}

		throw new UnauthorizedAccessException("Only the challenge creator or a league admin can confirm this outcome.");
	}

	private ChallengeListItem ToListItem(Challenge challenge)
	{
		var targetNames = challenge.TargetMemberIds
			.Select(targetMemberId => context.Members.TryGetValue(targetMemberId, out var member) ? member.DisplayName : "Unknown member")
			.ToArray();

		return new ChallengeListItem(
			challenge.Id,
			challenge.CreatedByUserId,
			challenge.Name,
			challenge.Description,
			challenge.TargetMemberIds,
			targetNames,
			challenge.AcceptedMemberIds,
			challenge.RejectedMemberIds,
			challenge.CompletedMemberIds,
			challenge.FailedMemberIds,
			challenge.PointsForSuccess,
			challenge.PointsForFailure,
			challenge.IsActive,
			challenge.CreatedAt,
			GetOutcomeItems(challenge));
	}

	private IReadOnlyCollection<ChallengeOutcomeItem> GetOutcomeItems(Challenge challenge)
	{
		return challenge.TargetMemberIds
			.Select(memberId =>
			{
				var memberName = context.Members.TryGetValue(memberId, out var member) ? member.DisplayName : "Unknown member";
				var allocation = context.Allocations.Values
					.Where(candidate => candidate.ChallengeId == challenge.Id && candidate.LeagueMemberId == memberId)
					.OrderByDescending(candidate => candidate.AwardedAt)
					.FirstOrDefault();
				var awardedBy = allocation is not null && context.Users.TryGetValue(allocation.AwardedByUserId, out var user)
					? user.Name
					: null;

				return new ChallengeOutcomeItem(
					memberId,
					memberName,
					GetMemberStatus(challenge, memberId),
					allocation?.AwardedAt,
					awardedBy,
					allocation?.Points);
			})
			.ToArray();
	}

	private static string GetMemberStatus(Challenge challenge, Guid memberId)
	{
		if (challenge.CompletedMemberIds.Contains(memberId))
		{
			return "Completed";
		}

		if (challenge.FailedMemberIds.Contains(memberId))
		{
			return "Failed";
		}

		if (challenge.RejectedMemberIds.Contains(memberId))
		{
			return "Rejected";
		}

		if (challenge.AcceptedMemberIds.Contains(memberId))
		{
			return "Accepted";
		}

		return "Open";
	}

	private PointAllocation GetOrCreatePenaltyAllocation(Guid leagueId, Guid userId, Guid memberId, Challenge challenge, string reasonPrefix)
	{
		var penaltyPoints = challenge.PointsForFailure > 0 ? -challenge.PointsForFailure : challenge.PointsForFailure;
		if (penaltyPoints == 0)
		{
			penaltyPoints = -Math.Abs(challenge.PointsForSuccess);
		}

		var existingPenalty = context.Allocations.Values
			.Where(allocation =>
				allocation.ChallengeId == challenge.Id &&
				allocation.LeagueMemberId == memberId &&
				allocation.Source == PointAllocationSource.AdminPenalty)
			.OrderByDescending(allocation => allocation.AwardedAt)
			.FirstOrDefault();
		if (existingPenalty is not null)
		{
			return existingPenalty;
		}

		var allocation = new PointAllocation
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			LeagueMemberId = memberId,
			ChallengeId = challenge.Id,
			Points = penaltyPoints,
			Reason = $"{reasonPrefix}: {challenge.Name}",
			Source = PointAllocationSource.AdminPenalty,
			AwardedByUserId = userId,
			AwardedAt = DateTime.UtcNow
		};

		context.Allocations[allocation.Id] = allocation;
		return allocation;
	}
}
