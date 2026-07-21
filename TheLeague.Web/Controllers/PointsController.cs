using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues/{leagueId:guid}")]
public class PointsController(IPointAllocationService allocationService) : ControllerBase
{
	[HttpGet("points-feed")]
	public async Task<IActionResult> ListFeed(Guid leagueId, CancellationToken cancellationToken) =>
		Ok(await allocationService.ListFeedAsync(leagueId, User.GetRequiredUserId(), cancellationToken));

	[HttpPost("allocations")]
	public async Task<IActionResult> CreateManualAllocation(Guid leagueId, CreateManualAllocationRequest request, CancellationToken cancellationToken) =>
		Created(
			$"/api/leagues/{leagueId}/points-feed",
			await allocationService.CreateManualAsync(leagueId, User.GetRequiredUserId(), request.LeagueMemberId, request.Points, request.Reason, cancellationToken));

	[HttpPut("allocations/{allocationId:guid}")]
	public async Task<IActionResult> UpdateAllocation(Guid leagueId, Guid allocationId, UpdateManualAllocationRequest request, CancellationToken cancellationToken) =>
		Ok(await allocationService.UpdateAsync(leagueId, User.GetRequiredUserId(), allocationId, request.LeagueMemberId, request.Points, request.Reason, cancellationToken));

	[HttpDelete("allocations/{allocationId:guid}")]
	public async Task<IActionResult> DeleteAllocation(Guid leagueId, Guid allocationId, CancellationToken cancellationToken)
	{
		await allocationService.DeleteAsync(leagueId, User.GetRequiredUserId(), allocationId, cancellationToken);
		return NoContent();
	}
}
