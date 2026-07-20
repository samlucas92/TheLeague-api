using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class LeagueAuthorisationService(LeagueDataContext context) : ILeagueAuthorisationService
{
	public Task<LeagueMember> GetRequiredMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		var member = context.Members.Values
			.Where(member =>
				member.LeagueId == leagueId &&
				member.UserId == userId &&
				member.Status == LeagueMemberStatus.Active)
			.OrderByDescending(member => member.Role)
			.ThenByDescending(member => member.JoinedAt)
			.FirstOrDefault();

		if (member is null)
		{
			throw new UnauthorizedAccessException("Active league membership is required.");
		}

		return Task.FromResult(member);
	}

	public async Task<LeagueMember> GetRequiredAdminMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		var member = await GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		if (member.Role is not (LeagueMemberRole.Admin or LeagueMemberRole.Owner))
		{
			throw new UnauthorizedAccessException("League admin access is required.");
		}

		return member;
	}

	public async Task<LeagueMember> GetRequiredOwnerMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		var member = await GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		if (member.Role != LeagueMemberRole.Owner)
		{
			throw new UnauthorizedAccessException("League owner access is required.");
		}

		return member;
	}

	public async Task<LeagueMember> GetRequiredPointApproverMembershipAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		var member = await GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		if (member.Role is not (LeagueMemberRole.PointApprover or LeagueMemberRole.Admin or LeagueMemberRole.Owner))
		{
			throw new UnauthorizedAccessException("Point approver access is required.");
		}

		return member;
	}
}
