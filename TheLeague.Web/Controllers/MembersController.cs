using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues/{leagueId:guid}/members")]
public class MembersController(ILeagueMemberService memberService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List(Guid leagueId, CancellationToken cancellationToken) =>
		Ok(await memberService.ListAsync(leagueId, User.GetRequiredUserId(), cancellationToken));

	[HttpPost("offline")]
	public async Task<IActionResult> AddOffline(Guid leagueId, AddOfflineMemberRequest request, CancellationToken cancellationToken) =>
		Created($"/api/leagues/{leagueId}/members", await memberService.AddOfflineMemberAsync(leagueId, User.GetRequiredUserId(), request.DisplayName, request.EmailAddress, request.Role, cancellationToken));

	[HttpPut("{memberId:guid}")]
	public async Task<IActionResult> Update(Guid leagueId, Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken) =>
		Ok(await memberService.UpdateAsync(leagueId, User.GetRequiredUserId(), memberId, request.DisplayName, request.EmailAddress, request.Role, cancellationToken));

	[HttpPut("{memberId:guid}/role")]
	public async Task<IActionResult> ChangeRole(Guid leagueId, Guid memberId, ChangeMemberRoleRequest request, CancellationToken cancellationToken) =>
		Ok(await memberService.ChangeRoleAsync(leagueId, User.GetRequiredUserId(), memberId, request.Role, cancellationToken));

	[HttpDelete("{memberId:guid}")]
	public async Task<IActionResult> Delete(Guid leagueId, Guid memberId, CancellationToken cancellationToken)
	{
		await memberService.DeleteAsync(leagueId, User.GetRequiredUserId(), memberId, cancellationToken);
		return NoContent();
	}

	[HttpPost("{memberId:guid}/link")]
	public async Task<IActionResult> LinkOffline(Guid leagueId, Guid memberId, LinkOfflineMemberRequest request, CancellationToken cancellationToken) =>
		Ok(await memberService.LinkOfflineMemberAsync(leagueId, User.GetRequiredUserId(), memberId, request.EmailAddress, cancellationToken));
}
