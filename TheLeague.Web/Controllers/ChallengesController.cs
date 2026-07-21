using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues/{leagueId:guid}/challenges")]
public class ChallengesController(IChallengeService challengeService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List(Guid leagueId, CancellationToken cancellationToken) =>
		Ok(await challengeService.ListAsync(leagueId, User.GetRequiredUserId(), cancellationToken));

	[HttpPost]
	public async Task<IActionResult> Create(Guid leagueId, CreateChallengeRequest request, CancellationToken cancellationToken)
	{
		var challenge = new Challenge
		{
			Name = request.Name,
			Description = request.Description,
			TargetMemberIds = request.TargetMemberIds?.ToList() ?? [],
			PointsForSuccess = request.PointsForSuccess ?? request.FixedPoints ?? 0,
			PointsForFailure = request.PointsForFailure ?? 0,
			ScoringType = request.ScoringType ?? ChallengeScoringType.Fixed,
			FixedPoints = request.FixedPoints,
			MinimumPoints = request.MinimumPoints,
			MaximumPoints = request.MaximumPoints,
			RepeatType = request.RepeatType,
			SubmissionLimit = request.SubmissionLimit,
			RequiresEvidence = request.RequiresEvidence,
			IsSecret = request.IsSecret
		};

		return Created($"/api/leagues/{leagueId}/challenges", await challengeService.CreateAsync(leagueId, User.GetRequiredUserId(), challenge, cancellationToken));
	}

	[HttpPut("{challengeId:guid}")]
	public async Task<IActionResult> Update(Guid leagueId, Guid challengeId, UpdateChallengeRequest request, CancellationToken cancellationToken)
	{
		var challenge = new Challenge
		{
			Name = request.Name,
			Description = request.Description,
			TargetMemberIds = request.TargetMemberIds?.ToList() ?? [],
			PointsForSuccess = request.PointsForSuccess,
			PointsForFailure = request.PointsForFailure,
			IsActive = request.IsActive
		};

		return Ok(await challengeService.UpdateAsync(leagueId, User.GetRequiredUserId(), challengeId, challenge, cancellationToken));
	}

	[HttpDelete("{challengeId:guid}")]
	public async Task<IActionResult> Delete(Guid leagueId, Guid challengeId, CancellationToken cancellationToken)
	{
		await challengeService.DeleteAsync(leagueId, User.GetRequiredUserId(), challengeId, cancellationToken);
		return NoContent();
	}

	[HttpPost("{challengeId:guid}/accept")]
	public async Task<IActionResult> Accept(Guid leagueId, Guid challengeId, CancellationToken cancellationToken) =>
		Ok(await challengeService.AcceptAsync(leagueId, User.GetRequiredUserId(), challengeId, cancellationToken));

	[HttpPost("{challengeId:guid}/reject")]
	public async Task<IActionResult> Reject(Guid leagueId, Guid challengeId, CancellationToken cancellationToken) =>
		Ok(await challengeService.RejectAsync(leagueId, User.GetRequiredUserId(), challengeId, cancellationToken));

	[HttpPost("{challengeId:guid}/complete")]
	public async Task<IActionResult> Complete(Guid leagueId, Guid challengeId, CancellationToken cancellationToken) =>
		Ok(await challengeService.CompleteAsync(leagueId, User.GetRequiredUserId(), challengeId, cancellationToken));

	[HttpPost("{challengeId:guid}/fail")]
	public async Task<IActionResult> Fail(Guid leagueId, Guid challengeId, CancellationToken cancellationToken) =>
		Ok(await challengeService.FailAsync(leagueId, User.GetRequiredUserId(), challengeId, cancellationToken));
}
