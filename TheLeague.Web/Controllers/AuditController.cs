using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues/{leagueId:guid}/audit")]
public class AuditController(ILeagueAuditService auditService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List(Guid leagueId, [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
		Ok(await auditService.ListAsync(leagueId, User.GetRequiredUserId(), take, cancellationToken));
}
