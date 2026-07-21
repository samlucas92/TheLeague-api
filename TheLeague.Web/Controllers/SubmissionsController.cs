using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues/{leagueId:guid}/submissions")]
public class SubmissionsController(IPointSubmissionService submissionService) : ControllerBase
{
	[HttpGet("mine")]
	public async Task<IActionResult> ListMine(Guid leagueId, CancellationToken cancellationToken) =>
		Ok(await submissionService.ListMineAsync(leagueId, User.GetRequiredUserId(), cancellationToken));

	[HttpGet("pending")]
	public async Task<IActionResult> ListPending(Guid leagueId, CancellationToken cancellationToken) =>
		Ok(await submissionService.ListPendingAsync(leagueId, User.GetRequiredUserId(), cancellationToken));

	[HttpPost]
	public async Task<IActionResult> Create(Guid leagueId, CreateSubmissionRequest request, CancellationToken cancellationToken) =>
		Created(
			$"/api/leagues/{leagueId}/submissions",
			await submissionService.CreateAsync(leagueId, User.GetRequiredUserId(), request.ChallengeId, request.LeagueMemberId, request.RequestedPoints, request.PublicReason, cancellationToken));

	[HttpPost("{submissionId:guid}/approve")]
	public async Task<IActionResult> Approve(Guid leagueId, Guid submissionId, ApproveSubmissionRequest request, CancellationToken cancellationToken) =>
		Ok(await submissionService.ApproveAsync(leagueId, User.GetRequiredUserId(), submissionId, request.ApprovedPoints, request.PublicReviewReason, request.AdminReviewNote, cancellationToken));

	[HttpPost("{submissionId:guid}/reject")]
	public async Task<IActionResult> Reject(Guid leagueId, Guid submissionId, RejectSubmissionRequest request, CancellationToken cancellationToken) =>
		Ok(await submissionService.RejectAsync(leagueId, User.GetRequiredUserId(), submissionId, request.PublicReviewReason, request.AdminReviewNote, cancellationToken));
}
