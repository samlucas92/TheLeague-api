using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class LeaderboardService(LeagueDataContext context, ILeagueAuthorisationService authorisationService) : ILeaderboardService
{
	public async Task<IReadOnlyCollection<LeaderboardRow>> GetAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		return await GetPublicAsync(leagueId, cancellationToken);
	}

	public Task<IReadOnlyCollection<LeaderboardRow>> GetPublicAsync(Guid leagueId, CancellationToken cancellationToken = default)
	{
		if (!context.Leagues.TryGetValue(leagueId, out var league))
		{
			throw new InvalidOperationException("League was not found.");
		}

		var rows = context.Members.Values
			.Where(member => member.LeagueId == leagueId && member.Status == LeagueMemberStatus.Active)
			.Select(member =>
			{
				var approvedPoints = context.Allocations.Values
					.Where(allocation => allocation.LeagueMemberId == member.Id)
					.Sum(allocation => allocation.Points);

				var pendingPoints = league.ShowPendingPointsOnLeaderboard
					? context.Submissions.Values
						.Where(submission => submission.LeagueMemberId == member.Id && submission.Status == PointSubmissionStatus.Pending)
						.Sum(submission => submission.RequestedPoints ?? 0)
					: 0;

				return new
				{
					Member = member,
					ApprovedPoints = approvedPoints,
					PendingPoints = pendingPoints
				};
			})
			.OrderByDescending(row => row.ApprovedPoints)
			.ThenBy(row => row.Member.DisplayName)
			.ToArray();

		var result = new List<LeaderboardRow>();
		var previousPoints = int.MinValue;
		var position = 0;
		for (var index = 0; index < rows.Length; index++)
		{
			if (rows[index].ApprovedPoints != previousPoints)
			{
				position = index + 1;
				previousPoints = rows[index].ApprovedPoints;
			}

			result.Add(new LeaderboardRow(position, rows[index].Member.Id, rows[index].Member.DisplayName, rows[index].ApprovedPoints, rows[index].PendingPoints));
		}

		return Task.FromResult<IReadOnlyCollection<LeaderboardRow>>(result);
	}
}
