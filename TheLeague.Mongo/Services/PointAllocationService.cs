using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class PointAllocationService(LeagueDataContext context, ILeagueAuthorisationService authorisationService) : IPointAllocationService
{
	public async Task<ManualPointsResult> CreateManualAsync(Guid leagueId, Guid userId, Guid leagueMemberId, int points, string reason, CancellationToken cancellationToken = default)
	{
		var requester = await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		ValidateManualRequest(leagueId, leagueMemberId, points, reason);

		if (requester.Role is not (LeagueMemberRole.Admin or LeagueMemberRole.Owner))
		{
			var submission = new PointSubmission
			{
				Id = Guid.NewGuid(),
				LeagueId = leagueId,
				ChallengeId = null,
				LeagueMemberId = leagueMemberId,
				SubmittedByUserId = userId,
				RequestedPoints = points,
				PublicReason = reason.Trim(),
				Status = PointSubmissionStatus.Pending,
				SubmittedAt = DateTime.UtcNow
			};

			context.Submissions[submission.Id] = submission;
			return new ManualPointsResult("Pending", null, submission);
		}

		var allocation = new PointAllocation
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			LeagueMemberId = leagueMemberId,
			Points = points,
			Reason = reason.Trim(),
			Source = points > 0 ? PointAllocationSource.AdminAward : PointAllocationSource.AdminPenalty,
			AwardedByUserId = userId,
			AwardedAt = DateTime.UtcNow
		};

		context.Allocations[allocation.Id] = allocation;
		return new ManualPointsResult("Approved", allocation, null);
	}

	public async Task<PointAllocation> UpdateAsync(Guid leagueId, Guid userId, Guid allocationId, Guid leagueMemberId, int points, string reason, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		ValidateManualRequest(leagueId, leagueMemberId, points, reason);

		if (!context.Allocations.TryGetValue(allocationId, out var allocation) || allocation.LeagueId != leagueId)
		{
			throw new InvalidOperationException("Point allocation was not found.");
		}

		allocation.LeagueMemberId = leagueMemberId;
		allocation.Points = points;
		allocation.Reason = reason.Trim();
		allocation.Source = allocation.Source is PointAllocationSource.AdminAward or PointAllocationSource.AdminPenalty
			? points > 0 ? PointAllocationSource.AdminAward : PointAllocationSource.AdminPenalty
			: PointAllocationSource.Adjustment;
		await context.Allocations.SaveAsync(allocation, cancellationToken);
		return allocation;
	}

	public async Task DeleteAsync(Guid leagueId, Guid userId, Guid allocationId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		if (!context.Allocations.TryGetValue(allocationId, out var allocation) || allocation.LeagueId != leagueId)
		{
			throw new InvalidOperationException("Point allocation was not found.");
		}

		await context.Allocations.RemoveAsync(allocationId, cancellationToken);
	}

	public async Task<IReadOnlyCollection<PointsFeedItem>> ListFeedAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		return await ListPublicFeedAsync(leagueId, cancellationToken);
	}

	public Task<IReadOnlyCollection<PointsFeedItem>> ListPublicFeedAsync(Guid leagueId, CancellationToken cancellationToken = default)
	{
		var items = context.Allocations.Values
			.Where(allocation => allocation.LeagueId == leagueId)
			.OrderByDescending(allocation => allocation.AwardedAt)
			.Select(allocation =>
			{
				var member = context.Members.TryGetValue(allocation.LeagueMemberId, out var allocationMember)
					? allocationMember
					: null;
				var awardedBy = context.Users.TryGetValue(allocation.AwardedByUserId, out var awardingUser)
					? awardingUser.Name
					: "Unknown";
				var challengeName = allocation.ChallengeId.HasValue && context.Challenges.TryGetValue(allocation.ChallengeId.Value, out var challenge)
					? challenge.Name
					: null;

				return new PointsFeedItem(
					allocation.Id,
					allocation.LeagueMemberId,
					member?.DisplayName ?? "Unknown member",
					allocation.Points,
					allocation.Reason,
					allocation.Source,
					challengeName,
					awardedBy,
					allocation.AwardedAt);
			})
			.ToArray();

		return Task.FromResult<IReadOnlyCollection<PointsFeedItem>>(items);
	}

	private void ValidateManualRequest(Guid leagueId, Guid leagueMemberId, int points, string reason)
	{
		if (!context.Members.TryGetValue(leagueMemberId, out var member) || member.LeagueId != leagueId || member.Status != LeagueMemberStatus.Active)
		{
			throw new InvalidOperationException("League member was not found.");
		}

		if (points == 0)
		{
			throw new InvalidOperationException("Points must be positive or negative.");
		}

		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new InvalidOperationException("A reason is required.");
		}
	}
}
