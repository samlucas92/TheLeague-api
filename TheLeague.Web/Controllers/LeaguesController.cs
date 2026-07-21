using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues")]
public class LeaguesController(
	ILeagueService leagueService,
	ILeagueAuthorisationService authorisationService,
	ILeagueMemberService memberService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List(CancellationToken cancellationToken) =>
		Ok(await leagueService.ListForUserAsync(User.GetRequiredUserId(), cancellationToken));

	[HttpPost]
	public async Task<IActionResult> Create(CreateLeagueRequest request, CancellationToken cancellationToken)
	{
		var league = await leagueService.CreateAsync(User.GetRequiredUserId(), request.Name, request.Description, request.PresetType, request.JoinMode, cancellationToken);
		return Created($"/api/leagues/{league.Id}", league);
	}

	[HttpGet("{leagueId:guid}")]
	public async Task<IActionResult> Get(Guid leagueId, CancellationToken cancellationToken)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, User.GetRequiredUserId(), cancellationToken);
		var league = await leagueService.GetAsync(leagueId, cancellationToken);
		return league is null ? NotFound() : Ok(league);
	}

	[HttpPost("join-preview")]
	public async Task<IActionResult> PreviewJoin(JoinPreviewRequest request, CancellationToken cancellationToken) =>
		Ok(await leagueService.PreviewJoinAsync(request.JoinCode, cancellationToken));

	[HttpPost("join")]
	public async Task<IActionResult> Join(JoinLeagueRequest request, CancellationToken cancellationToken) =>
		Ok(await memberService.JoinAsync(User.GetRequiredUserId(), request.JoinCode, request.DisplayName, cancellationToken));
}
