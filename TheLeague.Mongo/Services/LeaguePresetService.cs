using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class LeaguePresetService : ILeaguePresetService
{
	public Task ApplyPresetAsync(Guid leagueId, LeaguePresetType presetType, CancellationToken cancellationToken = default)
	{
		// Presets no longer create challenge rows. Challenges are player-to-player dares,
		// while normal scoring is handled through manual point allocations.
		return Task.CompletedTask;
	}
}
