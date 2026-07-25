using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class LeagueAuditService(LeagueDataContext context, ILeagueAuthorisationService authorisationService) : ILeagueAuditService
{
	public async Task RecordAsync(Guid leagueId, Guid performedByUserId, LeagueAuditAction action, string entityType, Guid? entityId, string summary, CancellationToken cancellationToken = default)
	{
		var entry = new LeagueAuditEntry
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			PerformedByUserId = performedByUserId,
			Action = action,
			EntityType = entityType,
			EntityId = entityId,
			Summary = summary.Trim(),
			CreatedAt = DateTime.UtcNow
		};

		context.AuditEntries[entry.Id] = entry;
		await context.AuditEntries.SaveAsync(entry, cancellationToken);
	}

	public async Task<IReadOnlyCollection<LeagueAuditItem>> ListAsync(Guid leagueId, Guid userId, int take = 50, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var limit = Math.Clamp(take, 1, 100);

		return context.AuditEntries.Values
			.Where(entry => entry.LeagueId == leagueId)
			.OrderByDescending(entry => entry.CreatedAt)
			.Take(limit)
			.Select(entry =>
			{
				var actorName = context.Users.TryGetValue(entry.PerformedByUserId, out var user)
					? user.Name
					: "Unknown user";

				return new LeagueAuditItem(
					entry.Id,
					entry.Action,
					entry.PerformedByUserId,
					actorName,
					entry.EntityType,
					entry.EntityId,
					entry.Summary,
					entry.CreatedAt);
			})
			.ToArray();
	}
}
