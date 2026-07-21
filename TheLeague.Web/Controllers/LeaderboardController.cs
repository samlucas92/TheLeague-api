using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues/{leagueId:guid}/leaderboard")]
public class LeaderboardController(ILeaderboardService leaderboardService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> Get(Guid leagueId, CancellationToken cancellationToken) =>
		Ok(await leaderboardService.GetAsync(leagueId, User.GetRequiredUserId(), cancellationToken));
}
