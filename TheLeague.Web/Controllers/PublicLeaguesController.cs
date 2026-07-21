using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;

namespace TheLeague.Web.Controllers;

[ApiController]
[Route("api/public/leagues")]
public class PublicLeaguesController(ILeagueService leagueService) : ControllerBase
{
	[HttpGet("{joinCode}")]
	public async Task<IActionResult> GetByJoinCode(string joinCode, CancellationToken cancellationToken) =>
		Ok(await leagueService.GetPublicViewAsync(joinCode, cancellationToken));
}
